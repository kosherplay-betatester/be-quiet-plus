using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darkmount.App.Macros;

/// <summary>
/// Macros live in %APPDATA%\OverMount\macros.json. <see cref="Load"/> and <see cref="Save"/> never throw:
/// an unreadable file loads as an empty list (and is copied to macros.json.corrupt so a later save can't destroy it),
/// and saving writes a temp file and moves it over the old one.
/// </summary>
public static class MacroStore
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverMount", "macros.json");

    static readonly object Gate = new();

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IEnumerable<Macro> macros) => JsonSerializer.Serialize(macros.ToList(), Json);

    /// <summary>Parses and normalises macros (for import). Throws <see cref="JsonException"/>/<see cref="NotSupportedException"/> on bad input.</summary>
    public static List<Macro> Deserialize(string json) => Normalise(JsonSerializer.Deserialize<List<Macro?>>(json, Json));

    public static List<Macro> Load(string path)
    {
        string json;
        try
        {
            if (!File.Exists(path)) return [];
            json = ReadWithRetry(path);
        }
        catch (Exception e)
        {
            Log.Write($"Macros file could not be read, starting empty: {e.Message}");
            return [];
        }

        try
        {
            return Deserialize(json);
        }
        catch (Exception e)
        {
            Log.Write($"Macros file is corrupt, starting empty (copy kept as .corrupt): {e.Message}");
            try { File.WriteAllText(path + ".corrupt", json); }
            catch (Exception copyError) { Log.Write($"Could not keep a copy of the macros file: {copyError.Message}"); }
            return [];
        }
    }

    /// <summary>Sharing violations (antivirus, cloud sync) are usually gone within a few hundred ms.</summary>
    static string ReadWithRetry(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < 5 && File.Exists(path))
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    /// <summary>Atomically replaces the file. Returns false (and logs) on failure.</summary>
    public static bool Save(string path, IEnumerable<Macro> macros)
    {
        var tmp = path + ".tmp";
        try
        {
            var json = Serialize(macros);
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(tmp, path, overwrite: true);
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Write($"Could not save macros: {e.Message}");
            try { File.Delete(tmp); }
            catch (Exception) { /* best effort */ }
            return false;
        }
    }

    /// <summary>Repairs what hand edits or older versions can leave behind; never drops a valid macro.</summary>
    static List<Macro> Normalise(List<Macro?>? macros)
    {
        var result = new List<Macro>();
        var ids = new HashSet<Guid>();
        foreach (var m in macros ?? [])
        {
            if (m is null) continue;
            m.Name ??= "";
            m.Steps = m.Steps?.Where(s => s is not null).ToList() ?? [];
            if (m.Trigger is null || !m.Trigger.IsValid)
            {
                m.Trigger = new();
                m.Enabled = false;
            }
            if (!Enum.IsDefined(m.Mode)) m.Mode = PlaybackMode.Once;
            m.RepeatCount = Math.Clamp(m.RepeatCount, 1, MacroPlayer.MaxRepeatCount);
            if (m.Id == Guid.Empty || !ids.Add(m.Id))
            {
                m.Id = Guid.NewGuid();
                ids.Add(m.Id);
            }
            result.Add(m);
        }
        return result;
    }
}
