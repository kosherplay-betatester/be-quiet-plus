using Darkmount.Keyboard;
using static Darkmount.Keyboard.BindingAction;

namespace Darkmount.App.Pages;

/// <summary>
/// Key remapping on the normal and Fn layers (every key, the 8 display keys and the 4 dock buttons), the master
/// binding switch, and the Game Mode key locks. Bindings are stored on the keyboard.
/// </summary>
public sealed class KeysPage : Ui.Page
{
    enum Kind { Default, Disabled, Key, FKey, Media, Mouse, Scroll, WindowsShortcut, Backlight, Character, Website }

    static readonly (Kind Kind, string Name)[] Kinds =
    [
        (Kind.Default, "Default (the key's normal function)"), (Kind.Disabled, "Disabled"), (Kind.Key, "Key or shortcut"),
        (Kind.FKey, "F13–F24 (for macros and apps)"), (Kind.Media, "Media"), (Kind.Mouse, "Mouse button"),
        (Kind.Scroll, "Mouse scroll"), (Kind.WindowsShortcut, "Windows shortcut"), (Kind.Backlight, "Lighting control"),
        (Kind.Character, "Special character"), (Kind.Website, "Open website"),
    ];

    readonly KeyboardService _keyboard;
    readonly KeyboardView _view = new() { Size = new Size(760, 290), Margin = new Padding(0, 6, 0, 6) };
    readonly RadioButton _normal = new() { Text = "Normal layer", AutoSize = true, Checked = true, ForeColor = Ui.Text };
    readonly RadioButton _fn = new() { Text = "Fn layer (while holding Fn)", AutoSize = true, ForeColor = Ui.Text };
    readonly CheckBox _enabled = Ui.Check("Custom key bindings enabled");
    readonly Label _keyTitle = new() { AutoSize = true, Font = Ui.Section, ForeColor = Ui.Accent };
    readonly Label _current = Ui.Note("", 640);
    readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Font = Ui.Body };
    readonly FlowLayoutPanel _editor = new() { AutoSize = true, WrapContents = true, MaximumSize = new Size(700, 0) };
    readonly Label _status = Ui.Note("", 700);

    // Editor controls (shown depending on the kind).
    readonly CheckBox _ctrl = Ui.Check("Ctrl"), _shift = Ui.Check("Shift"), _alt = Ui.Check("Alt"), _win = Ui.Check("Win");
    readonly ComboBox _usage = Combo(220), _fkey = Combo(120), _media = Combo(200), _mouse = Combo(160),
        _scroll = Combo(160), _shortcut = Combo(220), _backlight = Combo(220), _effect = Combo(160);
    readonly CheckBox _double = Ui.Check("Double click"), _hold = Ui.Check("While pressed");
    readonly NumericUpDown _autoFire = Ui.Number(0, 50);
    readonly TextBox _text = new() { Width = 360, Font = Ui.Body };

    // Game Mode locks
    readonly CheckBox _lockWin = Ui.Check("Windows key"), _lockAltTab = Ui.Check("Alt+Tab"), _lockAltF4 = Ui.Check("Alt+F4"),
        _lockShiftTab = Ui.Check("Shift+Tab"), _lockCaps = Ui.Check("Caps Lock"), _gameMode = Ui.Check("Game Mode is on");

    List<KeyBinding> _bindings = [];
    bool _loaded;

    public KeysPage(KeyboardService keyboard)
        : base("Keys", "Click a key, choose what it should do and press Apply. Bindings are stored on the keyboard. " +
                       "The dock buttons use these bindings when the dock is in its CUSTOM mode.")
    {
        _keyboard = keyboard;
        _view.KeyShapes = KeyGeometry.Layout(PhysicalLayout.Ansi, NumpadSide.Right, includeDock: true)
            .Select(k => new KeyShape(k.KeyId, new RectangleF(k.X, k.Y, k.Width, k.Height), ShortLabel(k.KeyId)))
            .ToList();
        _view.SelectionChanged += ShowSelected;
        _normal.CheckedChanged += (_, _) => { RefreshMarks(); ShowSelected(); };

        foreach (var (_, name) in Kinds) _kind.Items.Add(name);
        _kind.SelectedIndexChanged += (_, _) => BuildEditor();
        foreach (var u in HidUsage.Offered) _usage.Items.Add(new Item<byte>(u.Name, u.Usage));
        for (int n = 13; n <= 24; n++) _fkey.Items.Add(new Item<byte>($"F{n}", HidUsage.FKey(n)));
        foreach (var m in Enum.GetValues<MediaAction>().Where(m => m is not (MediaAction.None or MediaAction.SpecificSound)))
            _media.Items.Add(new Item<MediaAction>(Words(m.ToString()), m));
        foreach (var b in Enum.GetValues<MouseButtonKind>()) _mouse.Items.Add(new Item<MouseButtonKind>(Words(b.ToString()), b));
        foreach (var s in Enum.GetValues<MouseScrollDirection>()) _scroll.Items.Add(new Item<MouseScrollDirection>(Words(s.ToString()), s));
        foreach (var w in Enum.GetValues<WindowsShortcutAction>().Where(w => w != WindowsShortcutAction.None))
            _shortcut.Items.Add(new Item<WindowsShortcutAction>(Words(w.ToString()), w));
        foreach (var a in Enum.GetValues<BacklightAction>().Where(a => a != BacklightAction.None))
            _backlight.Items.Add(new Item<BacklightAction>(Words(a.ToString()), a));
        foreach (var e in LightingEffects.DarkMount) _effect.Items.Add(new Item<Effect>(e.Name, e.Effect));
        _backlight.SelectedIndexChanged += (_, _) => _effect.Visible = Selected<BacklightAction>(_backlight) == BacklightAction.SelectEffect;

        var layers = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        layers.Controls.AddRange([_normal, _fn, _enabled]);
        AddFull(layers);
        AddFull(_view);
        AddFull(_keyTitle);
        AddFull(_current);
        Row("Action", _kind);
        Row("", _editor);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        actions.Controls.Add(Ui.Button("Apply to key", async (_, _) => await ApplyKey(), primary: true));
        actions.Controls.Add(Ui.Button("Reload", async (_, _) => await Load()));
        actions.Controls.Add(Ui.Button("Restore all factory bindings", async (_, _) => await RestoreFactory()));
        AddFull(actions);
        AddFull(_status);

        Heading("Game Mode (Fn + Pause)");
        AddFull(Ui.Note("While Game Mode is on, these combinations are blocked so you can't leave the game by accident.", 640));
        var locks = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(700, 0) };
        locks.Controls.AddRange([_lockWin, _lockAltTab, _lockAltF4, _lockShiftTab, _lockCaps]);
        AddFull(locks);
        var gm = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        gm.Controls.Add(_gameMode);
        gm.Controls.Add(Ui.Button("Apply Game Mode settings", async (_, _) => await ApplyGameMode()));
        AddFull(gm);

        _enabled.Click += async (_, _) => await ApplyEnabled();
        _view.Select([KeyIds.CapsLock]);
        VisibleChanged += async (_, _) => { if (Visible && !_loaded) await Load(); };
    }

    Layer CurrentLayer => _fn.Checked ? Layer.Fn : Layer.Common;

    // ---------------------------------------------------------------- loading

    async Task Load()
    {
        _loaded = true;
        _status.Text = "Reading the keyboard's bindings…";
        try
        {
            var (bindings, enabled, locks, state) = await _keyboard.Run(q =>
            {
                var s = new KeyboardSettings(q);
                return (new Bindings(q).GetAll().ToList(), new Bindings(q).GetEnabled(), s.GetLocks(), s.GetState());
            });
            _bindings = bindings;
            _enabled.Checked = enabled;
            _lockWin.Checked = locks.HasFlag(GameModeLocks.Win);
            _lockAltTab.Checked = locks.HasFlag(GameModeLocks.AltTab);
            _lockAltF4.Checked = locks.HasFlag(GameModeLocks.AltF4);
            _lockShiftTab.Checked = locks.HasFlag(GameModeLocks.ShiftTab);
            _lockCaps.Checked = locks.HasFlag(GameModeLocks.CapsLock);
            _gameMode.Checked = state.HasFlag(KeyboardStateFlags.GameMode);
            RefreshMarks();
            ShowSelected();
            _status.Text = $"{_bindings.Count} custom binding(s) on the keyboard. Keys with a dot are remapped.";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    void RefreshMarks() => _view.SetMarked(_bindings.Where(b => b.Layer == CurrentLayer).Select(b => (int)b.KeyId));

    // ---------------------------------------------------------------- selected key

    byte? SelectedKey => _view.SelectedKeys.Count == 1 ? (byte)_view.SelectedKeys.First() : null;

    void ShowSelected()
    {
        if (SelectedKey is not { } id || KeyIds.Find(id) is not { } info) return;
        _keyTitle.Text = $"{info.Label}   ({(CurrentLayer == Layer.Fn ? "Fn layer" : "normal layer")})";
        bool locked = !KeyIds.IsRebindable(id, CurrentLayer);
        var binding = _bindings.FirstOrDefault(b => b.KeyId == id && b.Layer == CurrentLayer);
        _current.Text = locked ? "This key can't be changed (it controls the keyboard itself)."
            : binding is null ? "Currently: its normal function." : $"Currently: {Describe(binding.Action)}";
        _kind.Enabled = !locked;
        SelectKind(binding?.Action);
    }

    void SelectKind(BindingAction? action)
    {
        var kind = action switch
        {
            null => Kind.Default,
            Disabled => Kind.Disabled,
            StandardKey => Kind.Key,
            FKey f when f.Number >= 13 => Kind.FKey,
            FKey => Kind.Key,
            Media => Kind.Media,
            MouseButton => Kind.Mouse,
            MouseScroll => Kind.Scroll,
            WindowsShortcut => Kind.WindowsShortcut,
            Backlight => Kind.Backlight,
            AltCode => Kind.Character,
            OpenBrowser => Kind.Website,
            _ => Kind.Default,
        };
        _kind.SelectedIndex = Array.FindIndex(Kinds, k => k.Kind == kind);
        switch (action)
        {
            case StandardKey k:
                (_ctrl.Checked, _shift.Checked, _alt.Checked, _win.Checked) = (
                    (k.Modifiers & (KeyModifiers.LeftCtrl | KeyModifiers.RightCtrl)) != 0,
                    (k.Modifiers & (KeyModifiers.LeftShift | KeyModifiers.RightShift)) != 0,
                    (k.Modifiers & (KeyModifiers.LeftAlt | KeyModifiers.RightAlt)) != 0,
                    (k.Modifiers & (KeyModifiers.LeftWin | KeyModifiers.RightWin)) != 0);
                SelectValue(_usage, k.Usage);
                break;
            case FKey f when f.Number >= 13: SelectValue(_fkey, f.Usage); break;
            case FKey f: (_ctrl.Checked, _shift.Checked, _alt.Checked, _win.Checked) = (false, false, false, false); SelectValue(_usage, f.Usage); break;
            case Media m: SelectValue(_media, m.Action); break;
            case MouseButton b: SelectValue(_mouse, b.Button); _double.Checked = b.DoubleClick; _hold.Checked = b.WhilePressed; _autoFire.Value = Math.Min(50, (int)b.AutoFire); break;
            case MouseScroll s: SelectValue(_scroll, s.Direction); break;
            case WindowsShortcut w: SelectValue(_shortcut, w.Action); break;
            case Backlight b: SelectValue(_backlight, b.Action); if (b.Effect is { } e) SelectValue(_effect, e); break;
            case AltCode a: _text.Text = char.ConvertFromUtf32(a.CodePoint); break;
            case OpenBrowser o: _text.Text = o.Url; break;
        }
    }

    void BuildEditor()
    {
        _editor.Controls.Clear();
        switch (Kinds[Math.Max(0, _kind.SelectedIndex)].Kind)
        {
            case Kind.Key: _editor.Controls.AddRange([_ctrl, _shift, _alt, _win, _usage]); break;
            case Kind.FKey:
                _editor.Controls.Add(_fkey);
                _editor.Controls.Add(Ui.Note("Use these to trigger macros or apps from the Macros page.", 360));
                break;
            case Kind.Media: _editor.Controls.Add(_media); break;
            case Kind.Mouse:
                _editor.Controls.AddRange([_mouse, _double, _hold, new Label { Text = "Auto-fire clicks/s (0 = off)", AutoSize = true, ForeColor = Ui.Dim, Margin = new Padding(8, 8, 4, 0) }, _autoFire]);
                break;
            case Kind.Scroll: _editor.Controls.Add(_scroll); break;
            case Kind.WindowsShortcut: _editor.Controls.Add(_shortcut); break;
            case Kind.Backlight: _editor.Controls.AddRange([_backlight, _effect]); _effect.Visible = Selected<BacklightAction>(_backlight) == BacklightAction.SelectEffect; break;
            case Kind.Character:
                _editor.Controls.Add(_text);
                _editor.Controls.Add(Ui.Note("Type the character to insert, e.g. € or ©.", 300));
                break;
            case Kind.Website: _editor.Controls.Add(_text); break;
        }
        foreach (var c in new ComboBox[] { _usage, _fkey, _media, _mouse, _scroll, _shortcut, _backlight, _effect })
            if (c.SelectedIndex < 0 && c.Items.Count > 0) c.SelectedIndex = 0;
    }

    BindingAction? BuildAction()
    {
        switch (Kinds[_kind.SelectedIndex].Kind)
        {
            case Kind.Default: return null;
            case Kind.Disabled: return new Disabled();
            case Kind.Key:
                var mods = (_ctrl.Checked ? KeyModifiers.LeftCtrl : 0) | (_shift.Checked ? KeyModifiers.LeftShift : 0)
                         | (_alt.Checked ? KeyModifiers.LeftAlt : 0) | (_win.Checked ? KeyModifiers.LeftWin : 0);
                byte usage = Selected<byte>(_usage);
                return mods == KeyModifiers.None && HidUsage.IsFKey(usage) ? new FKey(usage) : new StandardKey(mods, usage);
            case Kind.FKey: return new FKey(Selected<byte>(_fkey));
            case Kind.Media: return new Media(Selected<MediaAction>(_media));
            case Kind.Mouse: return new MouseButton(Selected<MouseButtonKind>(_mouse), _hold.Checked, _double.Checked, (byte)_autoFire.Value);
            case Kind.Scroll: return new MouseScroll(Selected<MouseScrollDirection>(_scroll));
            case Kind.WindowsShortcut: return new WindowsShortcut(Selected<WindowsShortcutAction>(_shortcut));
            case Kind.Backlight:
                var a = Selected<BacklightAction>(_backlight);
                return new Backlight(a, a == BacklightAction.SelectEffect ? Selected<Effect>(_effect) : null);
            case Kind.Character:
                var text = _text.Text.Trim();
                if (text.Length == 0) throw new ArgumentException("Type the character first.");
                int cp = char.ConvertToUtf32(text, 0);
                if (cp > 0xFFFF) throw new ArgumentException("That character is not supported by the keyboard.");
                return new AltCode((ushort)cp);
            case Kind.Website:
                var url = _text.Text.Trim();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    throw new ArgumentException("Enter a full web address starting with https://");
                return new OpenBrowser(url);
        }
        return null;
    }

    // ---------------------------------------------------------------- applying

    async Task ApplyKey()
    {
        if (SelectedKey is not { } id) { _status.Text = "Click a key first."; return; }
        var layer = CurrentLayer;
        BindingAction? action;
        try { action = BuildAction(); }
        catch (ArgumentException e) { _status.Text = e.Message; return; }

        _status.Text = "Applying…";
        try
        {
            await _keyboard.Run(q =>
            {
                KeyboardBackupGuard.EnsureBackup(q);
                var b = new Bindings(q);
                if (action is null) b.Clear(id, layer);
                else b.SetBinding(new KeyBinding(id, layer, action));
            });
            _bindings.RemoveAll(x => x.KeyId == id && x.Layer == layer);
            if (action is not null) _bindings.Add(new KeyBinding(id, layer, action));
            RefreshMarks();
            ShowSelected();
            _status.Text = $"{KeyIds.Get(id).Label}: {(action is null ? "back to its normal function" : Describe(action))}.";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    async Task ApplyEnabled()
    {
        bool on = _enabled.Checked;
        try
        {
            await _keyboard.Run(q => { KeyboardBackupGuard.EnsureBackup(q); new Bindings(q).SetEnabled(on); });
            _status.Text = on ? "Custom key bindings are on." : "Custom key bindings are off (keys use their normal functions).";
        }
        catch (Exception e) { _status.Text = Friendly(e); _enabled.Checked = !on; }
    }

    async Task ApplyGameMode()
    {
        var locks = (_lockWin.Checked ? GameModeLocks.Win : 0) | (_lockAltTab.Checked ? GameModeLocks.AltTab : 0)
                  | (_lockAltF4.Checked ? GameModeLocks.AltF4 : 0) | (_lockShiftTab.Checked ? GameModeLocks.ShiftTab : 0)
                  | (_lockCaps.Checked ? GameModeLocks.CapsLock : 0);
        bool on = _gameMode.Checked;
        try
        {
            await _keyboard.Run(q =>
            {
                KeyboardBackupGuard.EnsureBackup(q);
                var s = new KeyboardSettings(q);
                s.SetLocks(locks);
                s.SetGameMode(on);
            });
            _status.Text = $"Game Mode {(on ? "on" : "off")}; blocked while on: {(locks == 0 ? "nothing" : locks.ToString())}.";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    async Task RestoreFactory()
    {
        if (MessageBox.Show(this, "Remove all custom key bindings and restore the be quiet! factory bindings?\n" +
                "(Your original bindings are backed up and can be restored from the Profiles page.)", "Darkmount Hub",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _status.Text = "Restoring factory bindings…";
        try
        {
            _bindings = await _keyboard.Run(q =>
            {
                KeyboardBackupGuard.EnsureBackup(q);
                var b = new Bindings(q);
                foreach (var existing in b.GetAll()) b.Clear(existing.KeyId, existing.Layer);
                foreach (var d in BindingDefaults.Factory) b.SetBinding(d);
                return b.GetAll().ToList();
            });
            RefreshMarks();
            ShowSelected();
            _status.Text = "Factory bindings restored.";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Compact key-cap text for the keyboard picture.</summary>
    static string ShortLabel(byte id)
    {
        if (id is >= KeyIds.DisplayKey1 and <= KeyIds.DisplayKey8) return $"B{id - KeyIds.DisplayKey1 + 1}";
        var label = KeyIds.Find(id)?.Label ?? "?";
        if (label.StartsWith("Numpad ", StringComparison.OrdinalIgnoreCase)) label = label[7..];
        return label switch
        {
            "Print Screen" or "PrintScreen" => "PrtSc", "Scroll Lock" or "ScrollLock" => "ScrLk", "Pause" or "Pause/Break" => "Pause",
            "Insert" => "Ins", "Delete" => "Del", "Page Up" or "PageUp" => "PgUp", "Page Down" or "PageDown" => "PgDn",
            "Escape" => "Esc", "Backspace" => "⌫", "Caps Lock" => "Caps", "Left Shift" or "Right Shift" => "Shift",
            "Left Ctrl" or "Right Ctrl" => "Ctrl", "Left Alt" => "Alt", "Left WIN" or "Right Win" or "Right WIN" => "Win",
            "Num Lock" or "NumLock" => "Num", "Play/Pause" => "Play", "Previous" => "Prev", "Enter" => "Enter",
            _ => label.Length > 6 ? label[..6] : label,
        };
    }

    public static string Describe(BindingAction action) => action switch
    {
        Disabled => "disabled",
        StandardKey k => (k.Modifiers == KeyModifiers.None ? "" : ModifierText(k.Modifiers) + "+") + HidUsage.NameOf(k.Usage),
        FKey f => $"F{f.Number}",
        Media m => Words(m.Action.ToString()),
        MouseButton b => $"mouse {Words(b.Button.ToString()).ToLowerInvariant()}{(b.DoubleClick ? " double click" : "")}{(b.WhilePressed ? " (held)" : "")}{(b.AutoFire > 0 ? $", auto-fire {b.AutoFire}/s" : "")}",
        MouseScroll s => $"scroll {s.Direction.ToString().ToLowerInvariant()}",
        WindowsShortcut w => Words(w.Action.ToString()),
        Backlight b => Words(b.Action.ToString()) + (b.Effect is { } e ? $" ({e})" : ""),
        AltCode a => $"types {char.ConvertFromUtf32(a.CodePoint)}",
        OpenBrowser o => $"opens {o.Url}",
        OpenFile f => $"opens {f.Path}",
        OpenFolder f => $"opens folder {f.Path}",
        _ => "a desktop-app action (kept as is)",
    };

    static string ModifierText(KeyModifiers m) => string.Join("+", new[]
    {
        (m & (KeyModifiers.LeftCtrl | KeyModifiers.RightCtrl)) != 0 ? "Ctrl" : null,
        (m & (KeyModifiers.LeftShift | KeyModifiers.RightShift)) != 0 ? "Shift" : null,
        (m & (KeyModifiers.LeftAlt | KeyModifiers.RightAlt)) != 0 ? "Alt" : null,
        (m & (KeyModifiers.LeftWin | KeyModifiers.RightWin)) != 0 ? "Win" : null,
    }.Where(s => s is not null));

    static string Words(string pascal) =>
        System.Text.RegularExpressions.Regex.Replace(pascal, "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

    sealed record Item<T>(string Name, T Value) { public override string ToString() => Name; }

    static ComboBox Combo(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Font = Ui.Body };

    static T Selected<T>(ComboBox c) => c.SelectedItem is Item<T> i ? i.Value : default!;

    static void SelectValue<T>(ComboBox c, T value)
    {
        for (int i = 0; i < c.Items.Count; i++)
            if (c.Items[i] is Item<T> item && EqualityComparer<T>.Default.Equals(item.Value, value)) { c.SelectedIndex = i; return; }
    }

    static string Friendly(Exception e) => e is KeyboardUnavailableException ? e.Message : $"Something went wrong: {e.Message}";
}
