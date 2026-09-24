using Darkmount.Keyboard;
using SkiaSharp;

namespace Darkmount.App.Pages;

/// <summary>The eight numpad LCD keys: show the current images, change them, back up and restore the originals.</summary>
public sealed class DisplayKeysPage : Ui.Page
{
    readonly KeyboardService _keyboard;
    readonly PictureBox[] _tiles = new PictureBox[DisplayKeys.Count];
    readonly Label _status = Ui.Note("", 640);
    bool _loaded;

    public DisplayKeysPage(KeyboardService keyboard)
        : base("Display keys", "The eight LCD keys on the numpad. Pick any picture (PNG, JPG, GIF first frame…); " +
                               "it is cropped to a square and stored on the keyboard. Your original images are backed up " +
                               "before the first change and can be restored at any time.")
    {
        _keyboard = keyboard;
        var grid = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, BackColor = Ui.Back };
        for (int i = 0; i < DisplayKeys.Count; i++)
        {
            int index = i;
            var tile = new PictureBox
            {
                Size = new Size(120, 120), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black,
                Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 4),
            };
            tile.Click += async (_, _) => await ChangeImage(index);
            _tiles[i] = tile;
            var cell = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0, 0, 18, 16) };
            cell.Controls.Add(new Label { Text = $"Key {i + 1}", ForeColor = Ui.Dim, Font = Ui.Body, AutoSize = true });
            cell.Controls.Add(tile);
            cell.Controls.Add(Ui.Button("Change…", async (_, _) => await ChangeImage(index)));
            grid.Controls.Add(cell);
        }
        AddFull(grid);

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        actions.Controls.Add(Ui.Button("Reload from keyboard", async (_, _) => await LoadImages(force: true)));
        actions.Controls.Add(Ui.Button("Restore original images", async (_, _) => await RestoreOriginals()));
        AddFull(actions);
        AddFull(_status);
        AddFull(Ui.Note("What each key does is set on the Keys page (keys B1–B8).", 640));

        VisibleChanged += async (_, _) => { if (Visible && !_loaded) await LoadImages(force: false); };
    }

    async Task LoadImages(bool force)
    {
        _loaded = true;
        // Show cached copies instantly; reading from the keyboard takes a few seconds per key.
        if (!force && Directory.Exists(DisplayKeyCache.Folder))
        {
            for (int i = 0; i < DisplayKeys.Count; i++) Show(i, DisplayKeyCache.Load(i));
            if (DisplayKeyCache.Complete) { _status.Text = "Showing the saved copies. Use \"Reload from keyboard\" to re-read."; return; }
        }

        _status.Text = "Reading the key images from the keyboard (takes a little while)…";
        try
        {
            await _keyboard.Run(q =>
            {
                var keys = new DisplayKeys(q);
                DisplayKeyBackup.BackupOnce(keys);
                for (int i = 0; i < DisplayKeys.Count; i++)
                {
                    var jpeg = keys.ReadStoredJpeg(i);
                    DisplayKeyCache.Save(i, jpeg);
                    int index = i;
                    BeginInvoke(() => Show(index, jpeg));
                }
            });
            _status.Text = "Up to date. Click a key to change its picture.";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    async Task ChangeImage(int index)
    {
        using var dlg = new OpenFileDialog
        {
            Title = $"Picture for display key {index + 1}",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        using var bitmap = SKBitmap.Decode(dlg.FileName);
        if (bitmap is null) { _status.Text = "That file is not a picture Darkmount Hub can read."; return; }
        var jpeg = DisplayKeys.EncodeForKey(bitmap);

        _status.Text = $"Writing key {index + 1}…";
        try
        {
            await _keyboard.Run(q =>
            {
                var keys = new DisplayKeys(q);
                DisplayKeyBackup.BackupOnce(keys); // originals first, never overwritten
                keys.WriteStoredJpeg(index, jpeg);
            });
            DisplayKeyCache.Save(index, jpeg);
            Show(index, jpeg);
            _status.Text = $"Key {index + 1} updated.";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    async Task RestoreOriginals()
    {
        if (!Directory.Exists(DisplayKeyBackup.DefaultFolder))
        {
            _status.Text = "No backup yet: the originals are saved automatically before the first change.";
            return;
        }
        if (MessageBox.Show(this, "Write your original display-key images back to the keyboard?", "Darkmount Hub",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        _status.Text = "Restoring the original images…";
        try
        {
            int n = await _keyboard.Run(q => DisplayKeyBackup.Restore(new DisplayKeys(q)));
            foreach (var i in Enumerable.Range(0, DisplayKeys.Count))
            {
                var path = Path.Combine(DisplayKeyBackup.DefaultFolder, $"key{i + 1}.jpg");
                if (File.Exists(path)) { var jpeg = File.ReadAllBytes(path); DisplayKeyCache.Save(i, jpeg); Show(i, jpeg); }
            }
            _status.Text = $"Restored {n} original image(s).";
        }
        catch (Exception e) { _status.Text = Friendly(e); }
    }

    void Show(int index, byte[]? storedJpeg)
    {
        var old = _tiles[index].Image;
        _tiles[index].Image = storedJpeg is null ? null : ToImage(storedJpeg);
        old?.Dispose();
    }

    static Image? ToImage(byte[] storedJpeg)
    {
        using var upright = DisplayKeys.DecodeStored(storedJpeg);
        if (upright is null) return null;
        using var png = upright.Encode(SKEncodedImageFormat.Png, 100);
        return Image.FromStream(new MemoryStream(png.ToArray()));
    }

    static string Friendly(Exception e) => e is KeyboardUnavailableException ? e.Message : $"Something went wrong: {e.Message}";
}

/// <summary>Local copies of the current key images so the page opens instantly.</summary>
static class DisplayKeyCache
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkmountHub", "display-keys-cache");

    static string PathOf(int i) => System.IO.Path.Combine(Folder, $"key{i + 1}.jpg");

    public static bool Complete => Enumerable.Range(0, DisplayKeys.Count).All(i => File.Exists(PathOf(i)) || File.Exists(PathOf(i) + ".empty"));

    public static byte[]? Load(int i) => File.Exists(PathOf(i)) ? File.ReadAllBytes(PathOf(i)) : null;

    public static void Save(int i, byte[]? jpeg)
    {
        Directory.CreateDirectory(Folder);
        if (jpeg is null) { File.WriteAllText(PathOf(i) + ".empty", ""); File.Delete(PathOf(i)); }
        else { File.WriteAllBytes(PathOf(i), jpeg); File.Delete(PathOf(i) + ".empty"); }
    }
}
