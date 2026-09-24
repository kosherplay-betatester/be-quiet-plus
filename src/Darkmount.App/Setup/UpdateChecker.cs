using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Darkmount.App.Setup;

/// <summary>A published release and its setup file.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Name, string Notes, string PageUrl, string? SetupUrl, long SetupSize, string? Sha256);

/// <summary>
/// Looks for a newer OverMount on the project's GitHub Releases page (one anonymous HTTPS request; nothing about this PC
/// is sent), downloads its setup file, checks its size and SHA-256, and runs it to update in place.
/// </summary>
public static class UpdateChecker
{
    public const string SetupAssetName = "OverMount-Setup.exe";
    static readonly string LatestApi = Installer.RepoUrl.Replace("https://github.com/", "https://api.github.com/repos/") + "/releases/latest";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OverMount", Installer.CurrentVersion.ToString(3)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>Reads GitHub's "latest release" JSON; null when it has no usable version tag.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        var root = JsonNode.Parse(json);
        var tag = root?["tag_name"]?.GetValue<string>();
        if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var version)) return null;
        if (root!["draft"]?.GetValue<bool>() == true || root["prerelease"]?.GetValue<bool>() == true) return null;
        var asset = root["assets"]?.AsArray().FirstOrDefault(a =>
            string.Equals(a?["name"]?.GetValue<string>(), SetupAssetName, StringComparison.OrdinalIgnoreCase));
        string? digest = asset?["digest"]?.GetValue<string>();
        return new ReleaseInfo(
            Installer.Normalise(version), tag, root["name"]?.GetValue<string>() ?? tag, root["body"]?.GetValue<string>() ?? "",
            root["html_url"]?.GetValue<string>() ?? Installer.RepoUrl + "/releases",
            asset?["browser_download_url"]?.GetValue<string>(), asset?["size"]?.GetValue<long>() ?? 0,
            digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null);
    }

    public static bool IsNewer(ReleaseInfo release, Version current) => release.Version > Installer.Normalise(current);

    /// <summary>The latest release, or null when GitHub can't be reached.</summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestApi, ct);
            if (!response.IsSuccessStatusCode) { Log.Write($"Update check: GitHub answered {(int)response.StatusCode}"); return null; }
            return Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Log.Write($"Update check failed: {e.Message}");
            return null;
        }
    }

    /// <summary>Downloads the release's setup file to %TEMP% and verifies it; returns its path.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (release.SetupUrl is null) throw new InvalidOperationException("This release has no setup file.");
        var dir = Path.Combine(Path.GetTempPath(), "OverMount-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"OverMount-Setup-{release.Version.ToString(3)}.exe");

        using (var response = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.SetupSize;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        var size = new FileInfo(path).Length;
        if (release.SetupSize > 0 && size != release.SetupSize)
        {
            File.Delete(path);
            throw new InvalidDataException($"The download is incomplete ({size:N0} of {release.SetupSize:N0} bytes).");
        }
        if (release.Sha256 is { } expected)
        {
            await using var file = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
            if (actual != expected)
            {
                file.Close();
                File.Delete(path);
                throw new InvalidDataException("The downloaded file doesn't match GitHub's checksum, so it was deleted.");
            }
        }
        return path;
    }

    /// <summary>Runs the downloaded setup silently: it closes this app, replaces it and starts the new version.</summary>
    public static void StartUpdate(string setupPath) =>
        Process.Start(new ProcessStartInfo(setupPath, "--install --silent") { UseShellExecute = false })?.Dispose();
}
