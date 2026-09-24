using Darkmount.Keyboard.Lamps;

// LampProbe — READ-ONLY dump of the Dark Mount's standard HID LampArray (VID 0x373F, PID 0x0001, usage page 0x59 = MI_03).
// Traffic: the report descriptor, GetFeature(LampArrayAttributesReport), and per lamp the LampAttributesRequest
// read-request (SetFeature) + GetFeature(LampAttributesResponseReport). Nothing else is sent: this tool has no
// colour, range-update or autonomous-mode operation, and it never opens the vendor interface (MI_02).
//
// Usage: LampProbe [--descriptor-out <file>]

string? descriptorOut = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--descriptor-out" when i + 1 < args.Length:
            descriptorOut = args[++i];
            break;
        case "-h" or "--help":
            Console.WriteLine("LampProbe [--descriptor-out <file>]   read-only LampArray dump (no write operations exist)");
            return 0;
        default:
            Console.Error.WriteLine($"LampProbe is read-only; '{args[i]}' is refused. Lighting changes (colours, range updates,");
            Console.Error.WriteLine("autonomous mode) are not implemented here and must be tested with the user watching.");
            return 2;
    }
}

using var device = LampArrayDevice.Open();
if (device is null)
{
    Console.WriteLine("No LampArray collection found (VID 0x373F, PID 0x0001, usage page 0x59).");
    return 1;
}

Console.WriteLine($"Device      {device.DevicePath}");
Console.WriteLine($"Buffers     {device.BufferLength} bytes per feature report (incl. report id)");
Console.WriteLine();
Console.WriteLine($"Report descriptor ({device.RawDescriptor.Length} bytes):");
for (int off = 0; off < device.RawDescriptor.Length; off += 32)
    Console.WriteLine($"  {off:X4}  {Convert.ToHexString(device.RawDescriptor, off, Math.Min(32, device.RawDescriptor.Length - off))}");
if (descriptorOut is not null)
{
    File.WriteAllBytes(descriptorOut, device.RawDescriptor);
    Console.WriteLine($"  saved to {descriptorOut}");
}

var layout = device.Layout;
Console.WriteLine();
Console.WriteLine(device.LayoutWarning ?? "Layout parsed from the device descriptor:");
string[] names = ["LampArrayAttributes", "LampAttributesRequest", "LampAttributesResponse", "LampMultiUpdate", "LampRangeUpdate", "LampArrayControl"];
foreach (var (name, report) in names.Zip(layout.Reports))
    Console.WriteLine($"  {name,-23} {report}");
Console.WriteLine($"  lamps per multi-update {layout.MaxLampsPerMultiUpdate}, intensity channel {(layout.HasIntensityChannel ? "yes" : "no")}");

var a = device.Attributes;
Console.WriteLine();
Console.WriteLine($"Attributes  {a.LampCount} lamps, bounding box {a.BoundingBoxWidth} x {a.BoundingBoxHeight} x {a.BoundingBoxDepth} um, " +
                  $"kind {a.Kind}, min update interval {a.MinUpdateIntervalMicroseconds} us");

var lamps = device.Lamps;
var map = device.Map;
Console.WriteLine();
Console.WriteLine("  id        x       y      z  latency  purposes      R/G/B/I       prog  binding  key");
foreach (var l in lamps)
{
    string levels = $"{l.RedLevelCount}/{l.GreenLevelCount}/{l.BlueLevelCount}/{(l.IntensityLevelCount?.ToString() ?? "-")}";
    Console.WriteLine($"  {l.Id,3} {l.PositionX,8} {l.PositionY,7} {l.PositionZ,6} {l.UpdateLatencyMicroseconds,8}  {l.Purposes,-12}  {levels,-12}  " +
                      $"{(l.IsProgrammable ? "yes" : "no"),4}  0x{l.InputBinding:X2}     {map.Describe(l.Id)}");
}

Console.WriteLine();
Console.WriteLine("Summary");
Console.WriteLine($"  lamps enumerated        {lamps.Count} (ids {(lamps.Count > 0 ? $"{lamps[0].Id}..{lamps[^1].Id}" : "-")})");
Console.WriteLine($"  bindings present        {lamps.Count(l => l.InputBinding != 0)} of {lamps.Count}; detected kind {LampMap.DetectBindingKind(lamps)}, used {map.BindingKind}");
Console.WriteLine($"  keys mapped             {map.Keys.Count} ({map.UsageToLamp.Count} with a HID usage); non-key lamps {map.UnmappedLamps.Count}");
var missing = DarkmountKeys.ById.Keys.Where(id => !map.KeyIdToLamp.ContainsKey(id)).Select(id => DarkmountKeys.ById[id].Name).ToList();
Console.WriteLine($"  key ids without a lamp  {(missing.Count == 0 ? "none" : string.Join(", ", missing))}");
Console.WriteLine($"  purposes                {string.Join(", ", lamps.GroupBy(l => l.Purposes).Select(g => $"{g.Key} x{g.Count()}"))}");
Console.WriteLine($"  update latency (us)     {string.Join(", ", lamps.GroupBy(l => l.UpdateLatencyMicroseconds).Select(g => $"{g.Key} x{g.Count()}"))}");
Console.WriteLine($"  programmable            {lamps.Count(l => l.IsProgrammable)} of {lamps.Count}");
if (lamps.Count > 0)
    Console.WriteLine($"  position range (um)     x {lamps.Min(l => l.PositionX)}..{lamps.Max(l => l.PositionX)}, " +
                      $"y {lamps.Min(l => l.PositionY)}..{lamps.Max(l => l.PositionY)}, z {lamps.Min(l => l.PositionZ)}..{lamps.Max(l => l.PositionZ)}");

var dl = DynamicLighting.Read(device.DevicePath);
Console.WriteLine();
Console.WriteLine($"Windows Dynamic Lighting (registry, read-only): global {Show(dl.GlobalEnabled)}, this device {Show(dl.DeviceEnabled)} " +
                  $"=> {(dl.WindowsMayDrive ? "Windows may drive the lamps (conflicts with direct HID control)" : "Windows is not driving the lamps")}");
return 0;

static string Show(bool? b) => b switch { true => "on", false => "off", null => "unknown" };
