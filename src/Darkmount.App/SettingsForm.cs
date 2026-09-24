using System.Text.Json;
using Darkmount.Screens;
using Darkmount.Sensors;
using SkiaSharp;

namespace Darkmount.App;

/// <summary>Main window: sidebar navigation, a live preview of the dock, and one page per settings area.</summary>
public sealed class SettingsForm : Form
{
    readonly AppSettings _edit;
    readonly Action<AppSettings> _apply;
    readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Ui.Back, AutoScroll = true };
    readonly FlowLayoutPanel _nav = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Ui.Panel, Padding = new Padding(10, 12, 10, 0), AutoScroll = true };
    readonly PictureBox _preview = new() { Size = new Size(200, 150), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Margin = new Padding(30, 6, 10, 10) };
    readonly Label _status = new() { AutoSize = true, ForeColor = Ui.Dim, Font = Ui.Body, Margin = new Padding(0, 9, 16, 0) };
    readonly List<(Button Button, Control Page)> _pages = [];

    // Dock screen
    readonly ComboBox _mode = Ui.Combo<ScreenMode>(), _default = Ui.Combo<ScreenKind>();
    readonly NumericUpDown _refresh = Ui.Number(1.5m, 60, 0.5m, 1);
    readonly TextBox _hotkey = new() { Width = 220, Font = Ui.Body };
    readonly CheckBox _autostart = Ui.Check("Start Darkmount Hub with Windows");

    // Animation
    readonly ComboBox _animKind = Ui.Combo<AnimationKind>();
    readonly TextBox _animPath = new() { Width = 360, Font = Ui.Body };

    // Alerts
    readonly CheckBox _cpuOn = Ui.Check("CPU temperature at or above"), _gpuOn = Ui.Check("GPU temperature at or above"),
        _ramOn = Ui.Check("RAM usage at or above"), _vramOn = Ui.Check("VRAM usage at or above"), _fpsOn = Ui.Check("In games, FPS below");
    readonly NumericUpDown _cpuMax = Ui.Number(50, 110), _gpuMax = Ui.Number(50, 110), _ramMax = Ui.Number(50, 100),
        _vramMax = Ui.Number(50, 100), _fpsMin = Ui.Number(5, 240), _fpsSec = Ui.Number(1, 30), _hold = Ui.Number(0, 120);

    // RGB effects
    readonly CheckBox _rgbOn = Ui.Check("Animate the keyboard's lights (Darkmount Hub drives every LED)"),
        _rgbAlert = Ui.Check("Flash the keyboard red while a dock alert is showing");
    readonly ComboBox _rgbEffect = Ui.Combo<Darkmount.Keyboard.Lamps.RgbEffectKind>();
    readonly TrackBar _rgbSpeed = new() { Minimum = 1, Maximum = 10, Width = 300, BackColor = Ui.Back },
        _rgbBrightness = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 300, BackColor = Ui.Back };
    readonly Pages.ColorButton _rgbColor = new(), _rgbEdge = new();
    readonly CheckBox _rgbEdgeAuto = Ui.Check("Edge lights follow the animation");

    // Sensors
    readonly NumericUpDown _gpuIndex = Ui.Number(0, 8);
    readonly TextBox _labels = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9.5f), Size = new Size(620, 300) };

    readonly Func<string>? _status2;
    readonly Label _statusText = new() { AutoSize = true, Font = new Font("Consolas", 10.5f), ForeColor = Ui.Text, Margin = new Padding(0, 4, 0, 0) };
    readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };

    /// <param name="status">Returns a multi-line status report for the Status page (null hides the page).</param>
    public SettingsForm(AppSettings current, Action<AppSettings> apply, Func<string>? status = null,
        KeyboardService? keyboard = null, Darkmount.Dock.DockConnection? dock = null, Macros.MacroManager? macros = null,
        ProfileManager? profiles = null, Func<string?>? currentGame = null,
        Func<Pages.HomeStatus>? home = null, Action<ScreenMode>? setMode = null, Action? togglePause = null)
    {
        _apply = apply;
        _status2 = status;
        _edit = Clone(current);

        Text = "Darkmount Hub";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1140, 720);
        MinimumSize = new Size(960, 600);
        BackColor = Ui.Back;
        ForeColor = Ui.Text;
        Font = Ui.Body;

        var sidebar = new Panel { Dock = DockStyle.Left, Width = 262, BackColor = Ui.Panel };
        var brand = new Label { Text = "Darkmount Hub", Font = Ui.Title, ForeColor = Ui.Text, AutoSize = true, Margin = new Padding(6, 4, 0, 14) };
        _nav.Controls.Add(brand);
        var previewBox = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 196, FlowDirection = FlowDirection.TopDown, BackColor = Ui.Panel };
        previewBox.Controls.Add(new Label { Text = "LIVE DOCK", ForeColor = Ui.Dim, Font = new Font("Segoe UI Semibold", 8.5f), AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        previewBox.Controls.Add(_preview);
        sidebar.Controls.Add(_nav);
        sidebar.Controls.Add(previewBox);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12), BackColor = Ui.Back };
        footer.Controls.Add(Ui.Button("Close", (_, _) => Close()));
        footer.Controls.Add(Ui.Button("Save", (_, _) => Save(), primary: true));
        footer.Controls.Add(_status);

        if (home is not null)
            AddPage("Home", new Pages.HomePage(home, setMode ?? (_ => { }), SelectPage, togglePause ?? (() => { })));
        AddPage("Dock screen", DockPage());
        AddPage("Animation", AnimationPage());
        AddPage("Alerts", AlertsPage());
        AddPage("RGB effects", RgbPage());
        AddPage("Sensors", SensorsPage());
        if (keyboard is not null)
        {
            AddPage("Lighting", new Pages.LightingPage(keyboard));
            AddPage("Keys", new Pages.KeysPage(keyboard));
            if (macros is not null) AddPage("Macros", new Pages.MacrosPage(macros, keyboard));
            if (profiles is not null) AddPage("Profiles", new Pages.ProfilesPage(profiles, currentGame ?? (() => null)));
            AddPage("Display keys", new Pages.DisplayKeysPage(keyboard));
        }
        if (dock is not null) AddPage("Dock settings", new Pages.DockSettingsPage(dock));
        if (_status2 is not null)
        {
            var sp = new Ui.Page("Status", "Live diagnostics: connection, uploads and sensor readings.");
            sp.AddFull(_statusText);
            AddPage("Status", sp);
            _statusTimer.Tick += (_, _) => _statusText.Text = _status2();
            _statusTimer.Start();
            _statusText.Text = _status2();
        }

        Controls.Add(_content);
        Controls.Add(footer);
        Controls.Add(sidebar);
        LoadValues();
        Select(0);
    }

    // ---------------------------------------------------------------- navigation

    /// <summary>Adds a sidebar entry and its page (later phases add keyboard pages here).</summary>
    public void AddPage(string title, Control page)
    {
        var button = new Button
        {
            Text = "   " + title, TextAlign = ContentAlignment.MiddleLeft, Width = 236, Height = 36, FlatStyle = FlatStyle.Flat,
            ForeColor = Ui.Text, BackColor = Ui.Panel, Font = Ui.Body, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 2),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Ui.PanelHover;
        int index = _pages.Count;
        button.Click += (_, _) => Select(index);
        _nav.Controls.Add(button);
        _pages.Add((button, page));
    }

    /// <summary>Shows the page with this sidebar title (used by the Home page's quick actions).</summary>
    public void SelectPage(string title)
    {
        int i = _pages.FindIndex(p => p.Button.Text.Trim() == title);
        if (i >= 0) Select(i);
    }

    void Select(int index)
    {
        _content.Controls.Clear();
        _content.Controls.Add(_pages[index].Page);
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Button.BackColor = i == index ? Ui.PanelHover : Ui.Panel;
            _pages[i].Button.ForeColor = i == index ? Ui.Accent : Ui.Text;
        }
    }

    /// <summary>Called from the pipeline thread with a frame copy; takes ownership.</summary>
    public void ShowPreview(SKBitmap frame)
    {
        using (frame)
        {
            if (IsDisposed || !IsHandleCreated) return;
            using var data = frame.Encode(SKEncodedImageFormat.Png, 100);
            var image = Image.FromStream(new MemoryStream(data.ToArray()));
            BeginInvoke(() =>
            {
                var old = _preview.Image;
                _preview.Image = image;
                old?.Dispose();
            });
        }
    }

    // ---------------------------------------------------------------- pages

    Control DockPage()
    {
        var p = new Ui.Page("Dock screen", "What the media dock shows and how Darkmount Hub behaves.");
        p.Row("Screen", _mode, "Auto shows stats while a game runs");
        p.Row("Default screen (Auto)", _default);
        p.Row("Refresh every", _refresh, "seconds (minimum ~5 s: each image takes ~2.2 s plus a 3 s rest for the dock)");
        p.Row("Switch-screen hotkey", _hotkey);
        p.Row("", _autostart);
        p.Heading("Tips");
        p.AddFull(Ui.Note("• If the dock is dark, press a dock button once: the dock only accepts images while awake.\n" +
                          "• When IO Center runs, Darkmount Hub pauses and gives the dock back automatically.\n" +
                          "• Exiting restores your own dock settings.", 640));
        return p;
    }

    Control AnimationPage()
    {
        var p = new Ui.Page("Animation", "Built-in themes, your own GIF or video, or a folder of pictures. " +
            "The dock redraws about every 2 seconds, so animations play as an ambient slideshow.");
        p.Row("Source", _animKind);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(_animPath);
        row.Controls.Add(Ui.Button("Browse…", (_, _) => Browse()));
        p.Row("File or folder", row);
        return p;
    }

    Control AlertsPage()
    {
        var p = new Ui.Page("Alerts", "A red banner appears on the dock (the stats move down so nothing is hidden) " +
            "and the dock refreshes immediately.");
        p.Row(_cpuOn, Unit(_cpuMax, "°C"));
        p.Row(_gpuOn, Unit(_gpuMax, "°C"));
        p.Row(_ramOn, Unit(_ramMax, "%"));
        p.Row(_vramOn, Unit(_vramMax, "%"));
        var fps = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        fps.Controls.AddRange([_fpsMin, Hint("FPS for at least"), _fpsSec, Hint("seconds")]);
        p.Row(_fpsOn, fps);
        p.Row("Keep alerts visible for", Unit(_hold, "seconds after recovery"));
        return p;
    }

    Control RgbPage()
    {
        var p = new Ui.Page("RGB effects", "Animations drawn by Darkmount Hub on the keyboard's 201 LEDs through the " +
            "standard Windows lighting interface: whole-keyboard effects run at up to 30 fps, per-key effects at about " +
            "7–14 fps. When off, the keyboard's own effect (Lighting page) comes back.");
        p.Row("", _rgbOn);
        p.Row("Effect", _rgbEffect, "Rainbow/Plasma: per key");
        p.Row("Speed", _rgbSpeed);
        p.Row("Brightness", _rgbBrightness);
        p.Row("Colour (Static, Breathing)", _rgbColor);
        var edge = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        edge.Controls.Add(_rgbEdgeAuto);
        edge.Controls.Add(_rgbEdge);
        p.Row("Edge lights", edge);
        p.Row("", _rgbAlert);
        p.AddFull(Ui.Note("CPU temperature colours the keyboard green (cool) through yellow to red (85 °C and above). " +
                          "If Windows 'Dynamic Lighting' is on for this keyboard, turn it off in Windows Settings first.", 640));
        return p;
    }

    Control SensorsPage()
    {
        var p = new Ui.Page("Sensors", "Data comes from MSI Afterburner (CPU, GPU, RAM, FPS), RivaTuner (game detection) and, " +
            "optionally, HWiNFO with 'Shared Memory Support' enabled (preferred when available).");
        p.Row("GPU", _gpuIndex, "0 = automatic (the busiest GPU)");
        p.Heading("Advanced: sensor names (JSON)");
        p.AddFull(_labels);
        return p;
    }

    static FlowLayoutPanel Unit(Control c, string unit)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        f.Controls.Add(c);
        f.Controls.Add(Hint(unit));
        return f;
    }

    static Label Hint(string text) => new() { Text = text, AutoSize = true, ForeColor = Ui.Dim, Font = Ui.Body, Margin = new Padding(6, 6, 6, 0) };

    void Browse()
    {
        if ((AnimationKind)_animKind.SelectedItem! == AnimationKind.Folder)
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK) _animPath.Text = dlg.SelectedPath;
            return;
        }
        using var file = new OpenFileDialog { Filter = "Animations and videos|*.gif;*.mp4;*.mkv;*.mov;*.wmv;*.avi;*.webm|All files|*.*" };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        _animPath.Text = file.FileName;
        _animKind.SelectedItem = Path.GetExtension(file.FileName).Equals(".gif", StringComparison.OrdinalIgnoreCase)
            ? AnimationKind.Gif : AnimationKind.Video;
    }

    // ---------------------------------------------------------------- load / save

    void LoadValues()
    {
        _mode.SelectedItem = _edit.Mode;
        _default.SelectedItem = _edit.DefaultScreen;
        _refresh.Value = Math.Clamp(_edit.RefreshMs / 1000m, 1.5m, 60);
        _hotkey.Text = _edit.Hotkey;
        _autostart.Checked = _edit.StartWithWindows;
        _animKind.SelectedItem = _edit.AnimationKind;
        _animPath.Text = _edit.AnimationPath ?? "";

        var a = _edit.Alerts;
        (_cpuOn.Checked, _cpuMax.Value) = (a.CpuTempEnabled, Ui.Clamp(_cpuMax, a.CpuTempMax));
        (_gpuOn.Checked, _gpuMax.Value) = (a.GpuTempEnabled, Ui.Clamp(_gpuMax, a.GpuTempMax));
        (_ramOn.Checked, _ramMax.Value) = (a.RamEnabled, Ui.Clamp(_ramMax, a.RamMaxPercent));
        (_vramOn.Checked, _vramMax.Value) = (a.VramEnabled, Ui.Clamp(_vramMax, a.VramMaxPercent));
        (_fpsOn.Checked, _fpsMin.Value, _fpsSec.Value) = (a.FpsEnabled, Ui.Clamp(_fpsMin, a.FpsMin), Ui.Clamp(_fpsSec, a.FpsSeconds));
        _hold.Value = Ui.Clamp(_hold, a.HoldSeconds);

        _rgbOn.Checked = _edit.RgbEnabled;
        _rgbAlert.Checked = _edit.RgbAlertFlash;
        _rgbEffect.SelectedItem = _edit.Rgb.Effect;
        _rgbSpeed.Value = Math.Clamp(_edit.Rgb.Speed, 1, 10);
        _rgbBrightness.Value = Math.Clamp(_edit.Rgb.Brightness, 0, 100);
        _rgbColor.Value = ToColor(_edit.Rgb.Color);
        _rgbEdgeAuto.Checked = _edit.Rgb.EdgeColor is null;
        _rgbEdge.Value = ToColor(_edit.Rgb.EdgeColor ?? "FFFFFF");

        _gpuIndex.Value = Ui.Clamp(_gpuIndex, _edit.Sensors.GpuIndex);
        _labels.Text = JsonSerializer.Serialize(_edit.Sensors, new JsonSerializerOptions { WriteIndented = true });
    }

    void Save()
    {
        if (!HotkeyWindow.TryParse(_hotkey.Text, out _, out _))
        {
            MessageBox.Show(this, "The hotkey must look like Ctrl+Alt+Shift+D.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SensorOptions sensors;
        try { sensors = JsonSerializer.Deserialize<SensorOptions>(_labels.Text) ?? new(); }
        catch (JsonException e)
        {
            MessageBox.Show(this, $"The sensor names are not valid JSON:\n{e.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        sensors.GpuIndex = (int)_gpuIndex.Value;

        var s = Clone(_edit);
        s.Mode = (ScreenMode)_mode.SelectedItem!;
        s.DefaultScreen = (ScreenKind)_default.SelectedItem!;
        s.DockIdleSeconds = 1;
        s.RefreshMs = (int)(_refresh.Value * 1000);
        s.Hotkey = _hotkey.Text.Trim();
        s.StartWithWindows = _autostart.Checked;
        s.AnimationKind = (AnimationKind)_animKind.SelectedItem!;
        s.AnimationPath = string.IsNullOrWhiteSpace(_animPath.Text) ? null : _animPath.Text.Trim();
        s.Alerts = new AlertSettings
        {
            CpuTempEnabled = _cpuOn.Checked, CpuTempMax = (double)_cpuMax.Value,
            GpuTempEnabled = _gpuOn.Checked, GpuTempMax = (double)_gpuMax.Value,
            RamEnabled = _ramOn.Checked, RamMaxPercent = (double)_ramMax.Value,
            VramEnabled = _vramOn.Checked, VramMaxPercent = (double)_vramMax.Value,
            FpsEnabled = _fpsOn.Checked, FpsMin = (double)_fpsMin.Value, FpsSeconds = (double)_fpsSec.Value,
            HoldSeconds = (double)_hold.Value,
        };
        s.Sensors = sensors;
        s.RgbEnabled = _rgbOn.Checked;
        s.RgbAlertFlash = _rgbAlert.Checked;
        s.Rgb = new Darkmount.Keyboard.Lamps.RgbEffectSettings
        {
            Effect = (Darkmount.Keyboard.Lamps.RgbEffectKind)_rgbEffect.SelectedItem!,
            Speed = _rgbSpeed.Value,
            Brightness = _rgbBrightness.Value,
            Color = Hex(_rgbColor.Value),
            EdgeColor = _rgbEdgeAuto.Checked ? null : Hex(_rgbEdge.Value),
        };
        _apply(s);
        _status.Text = $"Saved at {DateTime.Now:HH:mm:ss}";
    }

    static Color ToColor(string hex)
    {
        try { var c = Darkmount.Keyboard.Rgb.Parse(hex); return Color.FromArgb(c.R, c.G, c.B); }
        catch (Exception e) when (e is FormatException or ArgumentException) { return Color.OrangeRed; }
    }

    static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    static AppSettings Clone(AppSettings s) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.UseDarkTitleBar(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _statusTimer.Dispose();
        _preview.Image?.Dispose();
        base.OnFormClosed(e);
    }
}


