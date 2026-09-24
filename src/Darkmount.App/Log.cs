namespace Darkmount.App;

/// <summary>Daily log files in %LOCALAPPDATA%\DarkmountHub\logs, keeping the last 7.</summary>
public static class Log
{
    static readonly object Gate = new();
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkmountHub", "logs");

    public static string Directory => Dir;

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, $"{DateTime.Now:yyyy-MM-dd}.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { /* logging must never crash the app */ }
    }

    public static void Prune()
    {
        try
        {
            foreach (var f in new DirectoryInfo(Dir).GetFiles("*.log").OrderByDescending(f => f.Name).Skip(7)) f.Delete();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
