using Darkmount.App.Setup;

namespace Darkmount.App.Pages;

/// <summary>What the About page can ask the tray app to do.</summary>
public sealed record UpdateActions(Func<Task<ReleaseInfo?>> CheckNow, Action<ReleaseInfo> Install, Func<ReleaseInfo?> Latest);

/// <summary>Version, updates (check / install / automatic checks), links and uninstall.</summary>
public sealed class AboutPage : Ui.Page
{
    readonly UpdateActions _updates;
    readonly Func<AppSettings> _get;
    readonly Action<AppSettings> _apply;
    readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), ForeColor = Ui.Text, Margin = new Padding(0, 6, 0, 6) };
    readonly Button _check, _install;
    readonly CheckBox _auto = Ui.Check("Check for updates automatically (once a day, from GitHub)");
    ReleaseInfo? _release;

    public AboutPage(UpdateActions updates, Func<AppSettings> get, Action<AppSettings> apply)
        : base("About & updates", $"OverMount {Installer.CurrentVersion.ToString(3)} — a community app for the be quiet! Dark Mount " +
                                  "and Light Mount keyboards. Not made or endorsed by be quiet!.")
    {
        _updates = updates;
        _get = get;
        _apply = apply;
        _check = Ui.Button("Check for updates", async (_, _) => await Check(), primary: true);
        _install = Ui.Button("Update now", (_, _) => { if (_release is { } r) _updates.Install(r); });
        _install.Visible = false;

        Heading("Updates");
        AddFull(_status);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttons.Controls.AddRange([_check, _install]);
        AddFull(buttons);
        _auto.Checked = get().CheckForUpdates;
        _auto.CheckedChanged += (_, _) =>
        {
            var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(_get()))!;
            s.CheckForUpdates = _auto.Checked;
            _apply(s);
        };
        AddFull(_auto);
        AddFull(Ui.Note("The check asks GitHub for the latest release (nothing about your PC is sent). Updates are verified against " +
                        "GitHub's checksum before they are installed, and your settings, profiles and macros are kept.", 720));

        Heading("Installation");
        AddFull(Ui.Note(Installer.IsInstalledCopy ? $"Installed in {Installer.InstallDir} for your Windows account."
            : Installer.IsDeveloperBuild ? "Running from a development build." : "Running without installing (portable).", 720));
        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        links.Controls.AddRange([
            Ui.Button("Project page & guide", (_, _) => HomePage.Open(Installer.RepoUrl + "#readme")),
            Ui.Button("All releases", (_, _) => HomePage.Open(Installer.RepoUrl + "/releases")),
            Ui.Button("Report a problem", (_, _) => HomePage.Open(Installer.RepoUrl + "/issues")),
        ]);
        if (Installer.IsInstalledCopy)
            links.Controls.Add(Ui.Button("Uninstall…", (_, _) =>
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Installer.InstalledExe, "--uninstall") { UseShellExecute = false })?.Dispose()));
        AddFull(links);

        ShowRelease(updates.Latest(), checkedNow: false);
    }

    async Task Check()
    {
        _check.Enabled = false;
        _status.Text = "Checking GitHub…";
        try { ShowRelease(await _updates.CheckNow(), checkedNow: true); }
        finally { _check.Enabled = true; }
    }

    void ShowRelease(ReleaseInfo? release, bool checkedNow)
    {
        _release = release is not null && UpdateChecker.IsNewer(release, Installer.CurrentVersion) ? release : null;
        _install.Visible = _release?.SetupUrl is not null;
        if (_release is { } r) _install.Text = $"Update to {r.Version.ToString(3)}";
        _status.Text = _release is { } newer ? $"Version {newer.Version.ToString(3)} is available." :
            release is not null ? $"You have the latest version ({Installer.CurrentVersion.ToString(3)})." :
            checkedNow ? "Couldn't reach GitHub. Check your internet connection and try again." :
            $"Version {Installer.CurrentVersion.ToString(3)}.";
    }
}
