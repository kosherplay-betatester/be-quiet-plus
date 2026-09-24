using Darkmount.Dock;
using Darkmount.QLink;

namespace Darkmount.App.Pages;

/// <summary>The dock's own settings: menu colour, clock format, and idle behaviour when Darkmount Hub is not driving it.</summary>
public sealed class DockSettingsPage : Ui.Page
{
    readonly DockConnection _dock;
    readonly Button _color = Ui.Button("      ");
    readonly ComboBox _clock = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Font = Ui.Body };
    readonly ComboBox _idleMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Font = Ui.Body };
    readonly NumericUpDown _idle = Ui.Number(5, 240), _off = Ui.Number(0, 240);
    readonly Label _status = Ui.Note("", 640);
    Color _menuColor = Color.FromArgb(0xDC, 0x4D, 0x00);

    public DockSettingsPage(DockConnection dock)
        : base("Dock settings", "The media dock's own menu colour and clock, and what it shows when Darkmount Hub is " +
                                "paused or closed (while the app runs it keeps the screen on and shows your dashboard).")
    {
        _dock = dock;
        _clock.Items.AddRange(["24-hour", "12-hour"]);
        _idleMode.Items.AddRange(["Idle image", "Clock", "Nothing"]);
        _color.Click += (_, _) => PickColor();

        Row("Menu colour", _color);
        Row("Clock format", _clock);
        Heading("When Darkmount Hub is not running");
        Row("After inactivity show", _idleMode);
        Row("Show it after", _idle, "seconds");
        Row("Turn the screen off after", _off, "seconds (0 = never)");
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        actions.Controls.Add(Ui.Button("Apply", (_, _) => Apply(), primary: true));
        actions.Controls.Add(Ui.Button("Reset to be quiet! defaults", (_, _) => { Show(DockConfig.UserOriginal); Apply(); }));
        AddFull(actions);
        AddFull(_status);

        Show(dock.UserDockConfig ?? DockConfig.UserOriginal);
    }

    void Show(DockConfig c)
    {
        _menuColor = Color.FromArgb(c.MenuR, c.MenuG, c.MenuB);
        _color.BackColor = _menuColor;
        _color.FlatAppearance.BorderColor = _menuColor;
        _clock.SelectedIndex = c.Clock24h ? 0 : 1;
        _idleMode.SelectedIndex = c.Screensaver switch { ScreensaverMode.Image => 0, ScreensaverMode.Clock => 1, _ => 2 };
        _idle.Value = Ui.Clamp(_idle, c.IdleSeconds);
        _off.Value = Ui.Clamp(_off, c.ScreenOffSeconds);
    }

    void PickColor()
    {
        using var dlg = new ColorDialog { Color = _menuColor, FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _menuColor = dlg.Color;
        _color.BackColor = _menuColor;
        _color.FlatAppearance.BorderColor = _menuColor;
    }

    void Apply()
    {
        // A screen-off time of 0 would look like the app's own running config to the backup guard, so "never"
        // is stored as the maximum instead.
        int off = (int)_off.Value == 0 ? 240 : (int)_off.Value;
        var config = new DockConfig(_menuColor.R, _menuColor.G, _menuColor.B, _clock.SelectedIndex == 0,
            _idleMode.SelectedIndex switch { 0 => ScreensaverMode.Image, 1 => ScreensaverMode.Clock, _ => ScreensaverMode.Off },
            (int)_idle.Value, off);
        try
        {
            _dock.UpdateUserDockConfig(config);
            _status.Text = _dock.State == DockState.Ready
                ? "Saved. Menu colour and clock apply now; the idle settings apply when Darkmount Hub pauses or exits."
                : "Saved. They will be applied the next time the dock is released.";
        }
        catch (ArgumentException e) { _status.Text = e.Message; }
    }
}
