using Darkmount.App;

namespace Darkmount.Tests;

public class SettingsFormTests
{
    /// <summary>Renders every tab of the settings window to PNG (tests/.../snapshots/settings-*.png) for review.</summary>
    [Fact]
    public void Settings_window_renders_every_tab()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Log.Enabled = false;
#pragma warning disable WFO5001
                Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
                var dock = new Darkmount.Dock.DockConnection(() => null,
                    new Darkmount.Dock.DockConfigGuard(Path.Combine(Path.GetTempPath(), $"dmh-{Guid.NewGuid():N}.hex")), () => false);
                using var macros = new Darkmount.App.Macros.MacroManager(Path.Combine(Path.GetTempPath(), $"dmh-{Guid.NewGuid():N}.json"));
                using var form = new SettingsForm(new AppSettings(), _ => { }, () => "status", new KeyboardService(dock), dock, macros,
                    new ProfileManager(new KeyboardService(dock), () => new AppSettings(), _ => { }), () => null,
                    () => new Darkmount.App.Pages.HomeStatus("Dark Mount", "Stats (updates every ~5 s)", "Rainbow wave at 12 fps",
                        "Gaming", "3 active",
                        [
                            new("Keyboard connected", true, false, "Dark Mount is connected."),
                            new("IO Center is closed", false, false, "IO Center is running, so Darkmount Hub has paused.", "Close IO Center", () => { }),
                            new("HWiNFO shared memory (optional)", false, true, "Optional, more precise sensors."),
                        ]));
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-3000, -3000);
                form.Show();
                var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "snapshots");
                Directory.CreateDirectory(dir);
                var navButtons = FindAll<Button>(form).Where(b => b.Text.StartsWith("   ")).ToList();
                Assert.True(navButtons.Count >= 4);
                for (int i = 0; i < navButtons.Count; i++)
                {
                    navButtons[i].PerformClick();
                    Application.DoEvents();
                    Thread.Sleep(150);
                    Application.DoEvents();
                    using var bmp = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                    bmp.Save(Path.Combine(dir, $"settings-{i}.png"));
                }
                form.Close();
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }

    static IEnumerable<T> FindAll<T>(Control root) where T : Control =>
        root.Controls.Cast<Control>().SelectMany(c => (c is T t ? [t] : Enumerable.Empty<T>()).Concat(FindAll<T>(c)));
}
