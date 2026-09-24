using Darkmount.App;
using Darkmount.Keyboard;

namespace Darkmount.Tests;

public class ProfileTests
{
    public ProfileTests() => Log.Enabled = false;

    [Fact]
    public void Game_profiles_match_executables_case_insensitively()
    {
        var set = new ProfileSet
        {
            Profiles = [new Profile { Name = "Shooter", Games = ["cs2.exe", "valorant.exe"] }, new Profile { Name = "Work" }],
        };

        Assert.Equal("Shooter", set.ForGame("CS2.EXE")?.Name);
        Assert.Null(set.ForGame("notepad.exe"));
        Assert.Null(set.ForGame(null));
    }

    [Fact]
    public void Profiles_round_trip_through_json_including_keyboard_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmh-profiles-{Guid.NewGuid():N}.json");
        try
        {
            var set = new ProfileSet
            {
                DefaultProfile = "Work",
                Profiles =
                [
                    new Profile
                    {
                        Name = "Work", Games = ["x.exe"], RgbEnabled = true, Mode = ScreenMode.DockDefault,
                        Keyboard = new KeyboardSnapshot { LockMask = GameModeLocks.Win, LayerConfig = LightingEffects.FactoryDefault },
                    },
                ],
            };
            ProfileStore.Save(set, path);

            var back = ProfileStore.Load(path);
            var p = Assert.Single(back.Profiles);
            Assert.Equal("Work", back.DefaultProfile);
            Assert.Equal(ScreenMode.DockDefault, p.Mode);
            Assert.True(p.RgbEnabled);
            Assert.Equal(GameModeLocks.Win, p.Keyboard.LockMask);
            Assert.Equal(LightingEffects.FactoryDefault, p.Keyboard.LayerConfig);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Corrupt_profile_file_loads_as_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmh-profiles-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ broken");
        try { Assert.Empty(ProfileStore.Load(path).Profiles); }
        finally { File.Delete(path); }
    }
}
