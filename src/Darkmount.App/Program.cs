namespace Darkmount.App;

static class Program
{
    /// <summary>Signalled by "DarkmountHub --exit" to close the running instance cleanly (restoring the dock).</summary>
    public const string ExitEventName = @"Local\DarkmountHub.Exit";

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--exit", StringComparer.OrdinalIgnoreCase))
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out var running)) using (running) running.Set();
            return;
        }

        using var single = new Mutex(initiallyOwned: true, @"Local\DarkmountHub", out bool first);
        if (!first)
        {
            MessageBox.Show("Darkmount Hub is already running (see the tray icon).", "Darkmount Hub",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Log.Prune();
        Application.ThreadException += (_, e) => Log.Write($"UI error: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Fatal: {e.ExceptionObject}");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
