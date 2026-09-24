using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Darkmount.Screens;

namespace Darkmount.App.Sources;

/// <summary>Byte counters of one network adapter at one moment.</summary>
internal readonly record struct AdapterCounters(string Id, string Name, long BytesReceived, long BytesSent, bool HasGateway);

/// <summary>
/// Network throughput from the byte counters of the active adapters (up, not loopback or tunnel). When any adapter has
/// a default gateway only those are summed, so virtual switches (Hyper-V, WSL) that mirror host traffic are not
/// double counted. Rates are computed from the deltas between polls. Thread-safe; <see cref="Poll"/> never throws.
/// </summary>
public sealed class NetworkMonitor
{
    private const double MinIntervalSeconds = 0.2;

    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Dictionary<string, (long Rx, long Tx)>? _last;
    private TimeSpan _lastTime;
    private NetworkInfo? _lastResult;
    private string? _lastName;

    /// <summary>Takes a baseline sample immediately, so the first <see cref="Poll"/> already returns a rate.</summary>
    public NetworkMonitor() : this(sampleNow: true) { }

    internal NetworkMonitor(bool sampleNow)
    {
        if (sampleNow) Poll();
    }

    /// <summary>
    /// Rates since the previous poll; null on the very first sample (baseline) or when no adapter is up. Polls closer
    /// than 0.2 s apart return the previous result.
    /// </summary>
    public NetworkInfo? Poll()
    {
        try
        {
            return Update(ReadCounters(), _clock.Elapsed);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static List<AdapterCounters> ReadCounters()
    {
        var list = new List<AdapterCounters>();
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return list; }

        foreach (var ni in all)
        {
            try
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = ni.GetIPStatistics();
                bool gateway = ni.GetIPProperties().GatewayAddresses.Any(g =>
                    g.Address is not null && !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                list.Add(new AdapterCounters(ni.Id, ni.Name, stats.BytesReceived, stats.BytesSent, gateway));
            }
            catch (Exception)
            {
                // An adapter that disappears mid-enumeration is skipped.
            }
        }
        return list;
    }

    /// <summary>
    /// Drops filter-driver pseudo-interfaces ("Wi-Fi-QoS Packet Scheduler-0000", "Wi-Fi-WFP ... Filter-0000"): Windows
    /// lists them next to their adapter with identical byte counters, so counting them would multiply the rate.
    /// </summary>
    internal static IReadOnlyList<AdapterCounters> WithoutFilterModules(IReadOnlyList<AdapterCounters> adapters)
    {
        static bool HasModuleSuffix(string name) =>
            name.Length > 5 && name[^5] == '-' && name[^4..].All(char.IsAsciiDigit);

        if (!adapters.Any(a => HasModuleSuffix(a.Name))) return adapters;
        return adapters.Where(a => !HasModuleSuffix(a.Name) || !adapters.Any(parent =>
            parent.Id != a.Id && !HasModuleSuffix(parent.Name) &&
            a.Name.StartsWith(parent.Name + "-", StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Rate math, separated from the OS counters for tests. <paramref name="time"/> is a monotonic timestamp.</summary>
    internal NetworkInfo? Update(IReadOnlyList<AdapterCounters> adapters, TimeSpan time)
    {
        adapters = WithoutFilterModules(adapters);
        lock (_gate)
        {
            var previous = _last;
            double dt = (time - _lastTime).TotalSeconds;
            if (previous is not null && dt < MinIntervalSeconds) return _lastResult;

            var current = new Dictionary<string, (long, long)>(adapters.Count);
            foreach (var a in adapters) current[a.Id] = (a.BytesReceived, a.BytesSent);
            _last = current;
            _lastTime = time;

            if (previous is null || adapters.Count == 0)
            {
                _lastResult = null;
                return null;
            }

            bool anyGateway = adapters.Any(a => a.HasGateway);
            double rx = 0, tx = 0;
            string? busiest = null;
            long busiestBytes = 0;
            foreach (var a in adapters)
            {
                if (anyGateway && !a.HasGateway) continue;
                if (!previous.TryGetValue(a.Id, out var p)) continue; // new adapter: no baseline yet
                long dRx = Math.Max(0, a.BytesReceived - p.Rx), dTx = Math.Max(0, a.BytesSent - p.Tx); // counter reset → 0
                rx += dRx;
                tx += dTx;
                if (dRx + dTx > busiestBytes)
                {
                    busiestBytes = dRx + dTx;
                    busiest = a.Name;
                }
            }

            // Name the busiest adapter; while idle keep the previous name so it does not flicker.
            var counted = adapters.Where(a => !anyGateway || a.HasGateway).ToList();
            string? name = busiest
                           ?? (counted.Any(a => a.Name == _lastName) ? _lastName : counted.FirstOrDefault().Name);
            _lastName = name;
            _lastResult = new NetworkInfo(rx / dt, tx / dt, name);
            return _lastResult;
        }
    }
}
