using Darkmount.Keyboard;

namespace Darkmount.App.Pages;

/// <summary>Save, apply and auto-switch complete setups; restore the keyboard's original settings.</summary>
public sealed class ProfilesPage : Ui.Page
{
    readonly ProfileManager _manager;
    readonly Func<string?> _currentGame;
    readonly ProfileSet _set;
    readonly ListBox _list = new() { Width = 300, Height = 200, Font = Ui.Body, BackColor = Ui.Panel, ForeColor = Ui.Text, BorderStyle = BorderStyle.None };
    readonly TextBox _games = new() { Width = 360, Font = Ui.Body };
    readonly ComboBox _default = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Font = Ui.Body };
    readonly CheckBox _withImages = Ui.Check("Include display-key images");
    readonly Label _status = Ui.Note("", 700);

    public ProfilesPage(ProfileManager manager, Func<string?> currentGame)
        : base("Profiles", "A profile stores the keyboard's lighting, key bindings and Game Mode locks (optionally the " +
                           "display-key images) plus what the dock shows and the RGB effect. Profiles can switch automatically " +
                           "when a game starts.")
    {
        _manager = manager;
        _currentGame = currentGame;
        _set = new ProfileSet
        {
            DefaultProfile = manager.Set.DefaultProfile,
            Profiles = manager.Set.Profiles.Select(Clone).ToList(),
        };
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _games.TextChanged += (_, _) =>
        {
            if (Selected is { } p) p.Games = _games.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        buttons.Controls.AddRange([
            Ui.Button("Save current setup as…", async (_, _) => await CaptureNew(), primary: true),
            Ui.Button("Apply selected", async (_, _) => await ApplySelected()),
            Ui.Button("Update from current setup", async (_, _) => await UpdateSelected()),
            Ui.Button("Delete", (_, _) => DeleteSelected()),
        ]);
        buttons.Controls.Add(_withImages);
        var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        top.Controls.AddRange([_list, buttons]);
        AddFull(top);

        var games = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        games.Controls.AddRange([_games, Ui.Button("Add the running game", (_, _) => AddRunningGame())]);
        Row("Use automatically for games", games, null);
        AddFull(Ui.Note("Game executables separated by commas, e.g. cs2.exe, eldenring.exe.", 640));
        Row("When the game closes, switch to", _default);
        _default.SelectedIndexChanged += (_, _) => _set.DefaultProfile = _default.SelectedIndex <= 0 ? null : (string)_default.SelectedItem!;

        var save = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        save.Controls.AddRange([Ui.Button("Save profiles", (_, _) => SaveAll(), primary: true),
            Ui.Button("Restore my original keyboard settings", async (_, _) => await RestoreOriginal())]);
        AddFull(save);
        AddFull(_status);
        RefreshList();
    }

    Profile? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _set.Profiles.Count ? _set.Profiles[_list.SelectedIndex] : null;

    void RefreshList()
    {
        int sel = _list.SelectedIndex;
        _list.Items.Clear();
        foreach (var p in _set.Profiles)
            _list.Items.Add(p.Name + (p.Games.Count > 0 ? $"  ({string.Join(", ", p.Games)})" : "") + (p.Name == _manager.ActiveProfile ? "  ✓" : ""));
        if (sel >= 0 && sel < _list.Items.Count) _list.SelectedIndex = sel;
        else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _default.Items.Clear();
        _default.Items.Add("(keep the current settings)");
        foreach (var p in _set.Profiles) _default.Items.Add(p.Name);
        _default.SelectedIndex = Math.Max(0, _set.Profiles.FindIndex(p => p.Name == _set.DefaultProfile) + 1);
        ShowSelected();
    }

    void ShowSelected() => _games.Text = Selected is { } p ? string.Join(", ", p.Games) : "";

    async Task CaptureNew()
    {
        var name = Prompt("Profile name", $"Profile {_set.Profiles.Count + 1}");
        if (string.IsNullOrWhiteSpace(name)) return;
        _status.Text = "Reading the current setup from the keyboard…";
        try
        {
            var p = await _manager.Capture(name.Trim(), _withImages.Checked);
            _set.Profiles.RemoveAll(x => x.Name == p.Name);
            _set.Profiles.Add(p);
            RefreshList();
            _list.SelectedIndex = _set.Profiles.Count - 1;
            SaveAll();
            _status.Text = $"Saved \"{p.Name}\".";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    async Task UpdateSelected()
    {
        if (Selected is not { } old) return;
        try
        {
            var p = await _manager.Capture(old.Name, _withImages.Checked);
            p.Games = old.Games;
            _set.Profiles[_list.SelectedIndex] = p;
            SaveAll();
            _status.Text = $"\"{p.Name}\" updated from the current setup.";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    async Task ApplySelected()
    {
        if (Selected is not { } p) return;
        _status.Text = $"Applying \"{p.Name}\"…";
        try
        {
            int writes = await _manager.Apply(p);
            RefreshList();
            _status.Text = $"\"{p.Name}\" applied ({writes} change(s) written to the keyboard).";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    void DeleteSelected()
    {
        if (Selected is null) return;
        _set.Profiles.RemoveAt(_list.SelectedIndex);
        RefreshList();
        SaveAll();
    }

    void AddRunningGame()
    {
        if (Selected is not { } p) { _status.Text = "Select a profile first."; return; }
        if (_currentGame() is not { } game) { _status.Text = "No game is running right now (RivaTuner must see it)."; return; }
        if (!p.Games.Contains(game, StringComparer.OrdinalIgnoreCase)) p.Games.Add(game);
        ShowSelected();
        RefreshList();
    }

    void SaveAll()
    {
        _manager.Save(new ProfileSet { DefaultProfile = _set.DefaultProfile, Profiles = _set.Profiles.Select(Clone).ToList() });
        _status.Text = "Profiles saved.";
    }

    async Task RestoreOriginal()
    {
        if (KeyboardBackupGuard.Original is not { } original)
        {
            _status.Text = "There is no backup yet — nothing has been changed on the keyboard by Darkmount Hub.";
            return;
        }
        if (MessageBox.Show(this, "Write your original keyboard settings (lighting, key bindings, Game Mode locks and display-key " +
                "images, as they were before Darkmount Hub changed anything) back to the keyboard?", "Darkmount Hub",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _status.Text = "Restoring…";
        try
        {
            int writes = await _manager.Apply(new Profile { Name = "Original", Keyboard = original, Mode = ScreenMode.Auto });
            _status.Text = $"Original keyboard settings restored ({writes} change(s)).";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    static Profile Clone(Profile p) =>
        System.Text.Json.JsonSerializer.Deserialize<Profile>(System.Text.Json.JsonSerializer.Serialize(p))!;

    string? Prompt(string label, string initial)
    {
        using var f = new Form
        {
            Text = "Darkmount Hub", Width = 460, Height = 170, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, BackColor = Ui.Back, ForeColor = Ui.Text, Font = Ui.Body,
        };
        var box = new TextBox { Text = initial, Left = 16, Top = 40, Width = 410 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 250, Top = 80, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 346, Top = 80, Width = 80 };
        f.Controls.AddRange([new Label { Text = label, Left = 16, Top = 14, AutoSize = true }, box, ok, cancel]);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(FindForm()) == DialogResult.OK ? box.Text : null;
    }

    static string Friendly(Exception e) => e is KeyboardUnavailableException ? e.Message : $"Something went wrong: {e.Message}";
}
