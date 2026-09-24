using System.Diagnostics;
using Microsoft.Win32;

namespace Darkmount.App.Setup;

/// <summary>
/// OverMount used to be called "Darkmount Hub". On first start (and when installing) this closes a running old copy — it
/// restores the dock on exit — moves its settings, profiles, macros and backups to the new folders, and removes its
/// autostart entry, install folder, shortcuts and installed-apps entry.
/// </summary>
public static class LegacyMigration
{
    /// <summary>The old name as used in folders, the process and registry values.</summary>
    public const string LegacyName = "Darkmount" + "Hub";
    const string LegacyDisplayName = "Darkmount" + " Hub";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";

    /// <summary>Everything that is cheap to check on every start: old copy running, old data folders, old autostart.</summary>
    public static void OnStartup()
    {
        CloseLegacyInstance(TimeSpan.FromSeconds(15));
        MoveDataFolders();
        RemoveLegacyAutostart();
    }

    /// <summary>Also removes the old program folder and shortcuts (the installer calls this).</summary>
    public static void OnInstall()
    {
        OnStartup();
        RemoveLegacyInstall();
    }

    public static void CloseLegacyInstance(TimeSpan timeout)
    {
        var old = Process.GetProcessesByName(LegacyName);
        if (old.Length == 0) return;
        if (EventWaitHandle.TryOpenExisting($@"Local\{LegacyName}.Exit", out var exit)) using (exit) exit.Set();
        foreach (var p in old)
        {
            using (p)
            {
                try { if (!p.WaitForExit(timeout)) { p.Kill(); p.WaitForExit(5000); } }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
    }

    /// <summary>Moves %APPDATA%\DarkmountHub and %LOCALAPPDATA%\DarkmountHub to OverMount unless the new ones exist.</summary>
    public static void MoveDataFolders()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var from = Path.Combine(Environment.GetFolderPath(root), LegacyName);
            var to = Path.Combine(Environment.GetFolderPath(root), "OverMount");
            if (!Directory.Exists(from) || Directory.Exists(to)) continue;
            try { Directory.Move(from, to); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                try { CopyTree(from, to); }
                catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException) { Trace.WriteLine($"Moving {from} failed: {e2.Message} ({e.Message})"); }
            }
        }
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    static void RemoveLegacyAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(LegacyName) is not null) key.DeleteValue(LegacyName, throwOnMissingValue: false);
    }

    static void RemoveLegacyInstall()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", LegacyName);
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Trace.WriteLine($"Removing {dir} failed: {e.Message}"); }
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Startup })
        {
            var link = Path.Combine(Environment.GetFolderPath(folder), LegacyDisplayName + ".lnk");
            if (File.Exists(link)) File.Delete(link);
        }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallRoot + LegacyName, throwOnMissingSubKey: false);
    }
}
