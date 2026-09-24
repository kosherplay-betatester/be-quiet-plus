using System.Text.Json;
using Darkmount.Screens;
using SkiaSharp;

namespace Darkmount.App;

/// <summary>Settings window with a live preview of what the dock shows.</summary>
public sealed class SettingsForm : Form
{
    readonly AppSettings _edit;
    readonly Action<AppSettings> _apply;
    readonly PictureBox _preview = new() { Width = 320, Height = 240, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    // General
    readonly ComboBox _mode = Combo<ScreenMode>(), _default = Combo<ScreenKind>();
    readonly NumericUpDown _idle = Number(1, 4), _refresh = Number(1.5m, 60, 0.5m, 1);
    readonly TextBox _hotkey = new() { Width = 200 };
    readonly CheckBox _autostart = new() { Text = "Start with Windows", AutoSize = true };

    // Animation
    readonly ComboBox _animKind = Combo<AnimationKind>();
    readonly TextBox _animPath = new() { Width = 320 };

    // Alerts
    readonly CheckBox _cpuOn = Check("CPU temperature ≥"), _gpuOn = Check("GPU temperature ≥"), _ramOn = Check("RAM usage ≥"),
        _vramOn = Check("VRAM usage ≥"), _fpsOn = Check("FPS below");
    readonly NumericUpDown _cpuMax = Number(50, 110), _gpuMax = Number(50, 110), _ramMax = Number(50, 100), _vramMax = Number(50, 100),
        _fpsMin = Number(5, 240), _fpsSec = Number(1, 30), _hold = Number(0, 120);

    // Sensors
    readonly NumericUpDown _gpuIndex = Number(0, 8);
    readonly TextBox _labels = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), Dock = DockStyle.Fill };

    public SettingsForm(AppSettings current, Action<AppSettings> apply)
    {
        _apply = apply;
        _edit = Clone(current);

        Text = "Darkmount Hub settings";
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 520);
        MinimumSize = new Size(820, 480);
        Font = new Font("Segoe UI", 9.5f);

        _tabs.TabPages.Add(Page("Dock screen", GeneralPage()));
        _tabs.TabPages.Add(Page("Animation", AnimationPage()));
        _tabs.TabPages.Add(Page("Alerts", AlertsPage()));
        _tabs.TabPages.Add(Page("Sensors", SensorsPage()));

        var previewPanel = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 350, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };
        previewPanel.Controls.Add(new Label { Text = "Live dock preview", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        previewPanel.Controls.Add(_preview);
        previewPanel.Controls.Add(new Label
        {
            Text = "The dock redraws about every 2 seconds (hardware limit).\nPress a dock button once if its screen is dark.",
            AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(320, 0),
        });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 48, Padding = new Padding(8) };
        var save = new Button { Text = "Save", Width = 100, Height = 30 };
        var close = new Button { Text = "Close", Width = 100, Height = 30 };
        save.Click += (_, _) => Save();
        close.Click += (_, _) => Close();
        buttons.Controls.AddRange([close, save]);
        AcceptButton = save;
        CancelButton = close;

        Controls.Add(_tabs);
        Controls.Add(previewPanel);
        Controls.Add(buttons);
        Load();
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

    Control GeneralPage() => Grid(
        ("Screen", _mode),
        ("When no game runs (Auto)", _default),
        ("Dock menu stays up for (s)", _idle),
        ("Refresh every (s)", _refresh),
        ("Hotkey to switch screens", _hotkey),
        ("", _autostart));

    Control AnimationPage()
    {
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => Browse();
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([_animPath, browse]);
        var note = new Label
        {
            AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText,
            Text = "The dock can only show a new picture about every 2 seconds, so animations play as an ambient slideshow. " +
                   "GIFs and videos are decoded on the PC; a folder shows its pictures in turn.",
        };
        return Grid(("Source", _animKind), ("File or folder", row), ("", note));
    }

    Control AlertsPage() => Grid(
        ("", Pair(_cpuOn, _cpuMax, "°C")),
        ("", Pair(_gpuOn, _gpuMax, "°C")),
        ("", Pair(_ramOn, _ramMax, "%")),
        ("", Pair(_vramOn, _vramMax, "%")),
        ("", Pair(_fpsOn, _fpsMin, "FPS for", _fpsSec, "s (in games)")),
        ("Keep alerts visible for (s)", _hold));

    Control SensorsPage()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(Grid(("GPU index (0 = automatic)", _gpuIndex)));
        panel.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = SystemColors.GrayText,
            Text = "Data comes from MSI Afterburner (CPU, GPU, RAM, FPS), HWiNFO (optional, enable 'Shared Memory Support') " +
                   "and RivaTuner (game detection). Advanced: sensor label lists as JSON.",
        });
        panel.Controls.Add(_labels);
        return panel;
    }

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

    new void Load()
    {
        _mode.SelectedItem = _edit.Mode;
        _default.SelectedItem = _edit.DefaultScreen;
        _idle.Value = Math.Clamp(_edit.DockIdleSeconds, 1, 4);
        _refresh.Value = Math.Clamp(_edit.RefreshMs / 1000m, 1.5m, 60);
        _hotkey.Text = _edit.Hotkey;
        _autostart.Checked = _edit.StartWithWindows;
        _animKind.SelectedItem = _edit.AnimationKind;
        _animPath.Text = _edit.AnimationPath ?? "";

        var a = _edit.Alerts;
        (_cpuOn.Checked, _cpuMax.Value) = (a.CpuTempEnabled, Clamp(_cpuMax, a.CpuTempMax));
        (_gpuOn.Checked, _gpuMax.Value) = (a.GpuTempEnabled, Clamp(_gpuMax, a.GpuTempMax));
        (_ramOn.Checked, _ramMax.Value) = (a.RamEnabled, Clamp(_ramMax, a.RamMaxPercent));
        (_vramOn.Checked, _vramMax.Value) = (a.VramEnabled, Clamp(_vramMax, a.VramMaxPercent));
        (_fpsOn.Checked, _fpsMin.Value, _fpsSec.Value) = (a.FpsEnabled, Clamp(_fpsMin, a.FpsMin), Clamp(_fpsSec, a.FpsSeconds));
        _hold.Value = Clamp(_hold, a.HoldSeconds);

        _gpuIndex.Value = Clamp(_gpuIndex, _edit.Sensors.GpuIndex);
        _labels.Text = JsonSerializer.Serialize(_edit.Sensors, new JsonSerializerOptions { WriteIndented = true });
    }

    void Save()
    {
        if (!HotkeyWindow.TryParse(_hotkey.Text, out _, out _))
        {
            MessageBox.Show(this, "Hotkey must look like Ctrl+Alt+Shift+D.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Darkmount.Sensors.SensorOptions sensors;
        try { sensors = JsonSerializer.Deserialize<Darkmount.Sensors.SensorOptions>(_labels.Text) ?? new(); }
        catch (JsonException e)
        {
            MessageBox.Show(this, $"Sensor settings are not valid JSON:\n{e.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        sensors.GpuIndex = (int)_gpuIndex.Value;

        var s = Clone(_edit);
        s.Mode = (ScreenMode)_mode.SelectedItem!;
        s.DefaultScreen = (ScreenKind)_default.SelectedItem!;
        s.DockIdleSeconds = (int)_idle.Value;
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
        _apply(s);
    }

    static AppSettings Clone(AppSettings s)
    {
        var json = JsonSerializer.Serialize(s);
        return JsonSerializer.Deserialize<AppSettings>(json)!;
    }

    static decimal Clamp(NumericUpDown n, double v) => Math.Clamp((decimal)v, n.Minimum, n.Maximum);

    static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(8) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    static TableLayoutPanel Grid(params (string Label, Control Control)[] rows)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var (label, control) in rows)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 8) });
            control.Margin = new Padding(3, 5, 3, 5);
            t.Controls.Add(control);
        }
        return t;
    }

    static FlowLayoutPanel Pair(params object[] parts)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        foreach (var part in parts)
            p.Controls.Add(part as Control ?? new Label { Text = (string)part, AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        return p;
    }

    static ComboBox Combo<T>() where T : struct, Enum
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        foreach (var v in Enum.GetValues<T>()) c.Items.Add(v);
        return c;
    }

    static NumericUpDown Number(decimal min, decimal max, decimal step = 1, int decimals = 0) =>
        new() { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals, Width = 80 };

    static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 3) };

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _preview.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
