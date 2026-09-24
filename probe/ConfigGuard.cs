using System.Buffers.Binary;

namespace DockProbe;

/// <summary>
/// Keeps the user's real media dock settings in a file so tests always restore the true originals,
/// never a value a previous (crashed) test left on the keyboard.
/// </summary>
public static class ConfigGuard
{
    // Known-good settings read before any test ran: menu #DC4D00, 24h clock,
    // idle image after 30 s, screen off after 60 s.
    static readonly byte[] FactoryOriginal = Convert.FromHexString("DC4D0001021E003C00");

    static string BackupPath => Path.Combine(AppContext.BaseDirectory, "dock-config-backup.hex");

    /// <summary>Returns the saved original config, creating the backup from the device if it looks sane.</summary>
    public static byte[] Original(QLinkClient q)
    {
        if (File.Exists(BackupPath))
            return Convert.FromHexString(File.ReadAllText(BackupPath).Trim());

        var current = q.Send(QLinkClient.FeatMediaDock, 2);
        var saved = LooksLikeTestConfig(current) ? FactoryOriginal : current;
        File.WriteAllText(BackupPath, Convert.ToHexString(saved));
        Console.WriteLine($"Saved original dock config {Convert.ToHexString(saved)} to {BackupPath}");
        return saved;
    }

    /// <summary>Test configs use a 1 s idle delay and never-off screen; real ones never do.</summary>
    static bool LooksLikeTestConfig(byte[] c) =>
        BinaryPrimitives.ReadUInt16LittleEndian(c.AsSpan(5)) < 5 || BinaryPrimitives.ReadUInt16LittleEndian(c.AsSpan(7)) == 0;

    /// <summary>Test settings: show the image slot after 1 s, never turn the screen off.</summary>
    public static byte[] Quick(byte[] original)
    {
        var quick = (byte[])original.Clone();
        quick[4] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(quick.AsSpan(5), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(quick.AsSpan(7), 0);
        return quick;
    }
}
