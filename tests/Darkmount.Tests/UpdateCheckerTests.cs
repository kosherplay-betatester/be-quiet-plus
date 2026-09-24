using Darkmount.App.Setup;

namespace Darkmount.Tests;

public class UpdateCheckerTests
{
    const string Latest = """
        {"tag_name":"v1.2.0","name":"OverMount 1.2.0","body":"New: things","html_url":"https://github.com/o/r/releases/tag/v1.2.0",
         "draft":false,"prerelease":false,
         "assets":[{"name":"notes.txt","browser_download_url":"https://x/notes.txt","size":10},
                   {"name":"OverMount-Setup.exe","browser_download_url":"https://x/OverMount-Setup.exe","size":1234,
                    "digest":"sha256:ABCDEF0123"}]}
        """;

    [Fact]
    public void Latest_release_json_is_read()
    {
        var r = UpdateChecker.Parse(Latest)!;

        Assert.Equal(new Version(1, 2, 0), r.Version);
        Assert.Equal("v1.2.0", r.Tag);
        Assert.Equal("New: things", r.Notes);
        Assert.Equal("https://x/OverMount-Setup.exe", r.SetupUrl);
        Assert.Equal(1234, r.SetupSize);
        Assert.Equal("abcdef0123", r.Sha256);
    }

    [Fact]
    public void Releases_without_the_setup_file_have_no_download()
    {
        var r = UpdateChecker.Parse("""{"tag_name":"1.3.1","assets":[]}""")!;
        Assert.Null(r.SetupUrl);
        Assert.Null(r.Sha256);
        Assert.Equal(new Version(1, 3, 1), r.Version);
    }

    [Theory]
    [InlineData("""{"tag_name":"latest"}""")]
    [InlineData("""{"tag_name":"v2.0.0","prerelease":true}""")]
    [InlineData("""{"tag_name":"v2.0.0","draft":true}""")]
    public void Unusable_releases_are_ignored(string json) => Assert.Null(UpdateChecker.Parse(json));

    [Fact]
    public void Versions_compare_by_major_minor_patch()
    {
        var r = UpdateChecker.Parse(Latest)!;
        Assert.True(UpdateChecker.IsNewer(r, new Version(1, 1, 9, 0)));
        Assert.False(UpdateChecker.IsNewer(r, new Version(1, 2, 0, 0))); // assembly versions have a 4th part
        Assert.False(UpdateChecker.IsNewer(r, new Version(1, 3)));
        Assert.Equal(new Version(1, 2, 0), Installer.Normalise(new Version(1, 2)));
    }
}
