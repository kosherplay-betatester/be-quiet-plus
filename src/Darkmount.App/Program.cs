namespace Darkmount.App;

static class Program
{
    /// <summary>Signalled by "OverMount --exit" to close the running instance cleanly (restoring the dock).</summary>
    public const string ExitEventName = @"Local\OverMount.Exit";

    [STAThread]
    static void Main(string[] args)
    {
        bool Has(string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);
        if (Has("--exit"))
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out var running)) using (running) running.Set();
            return;
        }

        Setup.LegacyMigration.OnStartup(); // "Darkmount Hub" → OverMount: before anything reads settings or writes logs
        Application.ThreadException += (_, e) => Log.Write($"UI error: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Fatal: {e.ExceptionObject}");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); // before any window exists

        // The single-file release is also its own installer: started from anywhere but the install folder it offers to install.
        if (Has("--uninstall") || Has("--install") || (!Setup.Installer.IsInstalledCopy && !Setup.Installer.IsDeveloperBuild
                                                        && !Has("--portable") && !Has("--autostart")))
        {
            InitialiseUi();
            if (Has("--uninstall")) { Setup.SetupForm.Uninstall(silent: Has("--silent")); return; }
            if (Has("--install") && Has("--silent")) { Setup.SetupForm.InstallSilently(); return; }
            if (Setup.SetupForm.Run() != Setup.SetupResult.RunPortable) return;
        }

        using var single = new Mutex(initiallyOwned: true, @"Local\OverMount", out bool first);
        if (!first)
        {
            MessageBox.Show("OverMount is already running (see the tray icon).", "OverMount",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Log.Prune();
        InitialiseUi();
        Application.Run(new TrayApp());
    }

    static bool _uiReady;

    static void InitialiseUi()
    {
        if (_uiReady) return;
        _uiReady = true;
        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // dark mode for common controls is marked experimental
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
    }
}
