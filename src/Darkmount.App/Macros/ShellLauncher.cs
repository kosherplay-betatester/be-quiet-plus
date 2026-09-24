using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Darkmount.App.Macros;

/// <summary>
/// <see cref="IShell"/> over Process.Start with UseShellExecute. Every target is validated first and rejected with a
/// <see cref="MacroStepException"/>: programs/files must exist (full path, or a bare program name found on PATH),
/// folders must exist, and only http/https URLs are opened. Environment variables such as %USERPROFILE% are expanded.
/// </summary>
public sealed class ShellLauncher(Action<ProcessStartInfo>? start = null) : IShell
{
    readonly Action<ProcessStartInfo> _start = start ?? StartProcess;

    public void Launch(string path, string? arguments, string? workingDirectory)
    {
        var file = ResolveProgram(path) ?? throw new MacroStepException($"Program or file not found: {path}");
        string? cwd;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            cwd = Path.GetDirectoryName(file);
        }
        else
        {
            cwd = Expand(workingDirectory);
            if (!Path.IsPathFullyQualified(cwd) || !Directory.Exists(cwd))
                throw new MacroStepException($"Working folder not found: {workingDirectory}");
        }
        _start(new ProcessStartInfo(file)
        {
            Arguments = arguments ?? "",
            WorkingDirectory = cwd ?? "",
            UseShellExecute = true,
        });
    }

    public void OpenUrl(string url)
    {
        if (!TryNormaliseUrl(url, out var uri)) throw new MacroStepException($"Only http:// and https:// links can be opened: {url}");
        _start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public void OpenFolder(string path)
    {
        var dir = string.IsNullOrWhiteSpace(path) ? "" : Expand(path);
        if (!Path.IsPathFullyQualified(dir) || !Directory.Exists(dir)) throw new MacroStepException($"Folder not found: {path}");
        _start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    /// <summary>True for absolute http/https URLs with a host.</summary>
    public static bool IsAllowedUrl(string? url) => TryNormaliseUrl(url, out _);

    public static bool TryNormaliseUrl(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(parsed.Host)) return false;
        uri = parsed;
        return true;
    }

    /// <summary>
    /// Full path of an existing program/file: either a fully qualified path, or a bare name ("notepad", "cmd.exe")
    /// searched on PATH with PATHEXT. Relative paths with folders are refused. Null when not found.
    /// </summary>
    public static string? ResolveProgram(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = Expand(path);
        if (p.Length == 0) return null;
        if (Path.IsPathFullyQualified(p)) return File.Exists(p) ? p : null;
        if (p.IndexOfAny(['\\', '/', ':']) >= 0) return null;

        string[] extensions = Path.HasExtension(p)
            ? [""]
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in dirs)
        {
            if (!Path.IsPathFullyQualified(dir)) continue;
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, p + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    static string Expand(string path) => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"').Trim());

    static void StartProcess(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info)?.Dispose();
        }
        catch (Win32Exception e)
        {
            throw new MacroStepException($"Could not open {info.FileName}: {e.Message}", e);
        }
    }
}
