using Darkmount.Screens;
using Darkmount.Sensors;
using SkiaSharp;

namespace Darkmount.Tests;

public class AnimationTests
{
    private static readonly ScreenContext Ctx = new() { Snapshot = new Snapshot(), History = new MetricHistory() };

    private static byte[] Frame(AnimationScreen screen, string? saveAs = null)
    {
        using var bmp = DockRenderer.Render(screen, Ctx);
        ScreenRenderTests.AssertValidFrame(bmp);
        if (saveAs != null) ScreenRenderTests.Save(bmp, saveAs);
        return bmp.Bytes;
    }

    private static SKColor CenterPixel(AnimationScreen screen)
    {
        using var bmp = DockRenderer.Render(screen, Ctx);
        return bmp.GetPixel(160, 120);
    }

    [Theory]
    [InlineData(AnimationKind.Plasma)]
    [InlineData(AnimationKind.Matrix)]
    [InlineData(AnimationKind.Starfield)]
    public void Theme_ConsecutiveFramesDiffer(AnimationKind kind)
    {
        using var screen = new AnimationScreen(AnimationSources.Create(kind, null));
        Assert.Equal("Animation", screen.Name);
        string name = "anim-" + kind.ToString().ToLowerInvariant();
        var a = Frame(screen, name + "-1");
        var b = Frame(screen, name + "-2");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Theme_IsDeterministic()
    {
        using var s1 = new AnimationScreen(AnimationSources.Create(AnimationKind.Starfield, null));
        using var s2 = new AnimationScreen(AnimationSources.Create(AnimationKind.Starfield, null));
        Assert.Equal(Frame(s1), Frame(s2));
    }

    [Fact]
    public void Gif_AdvancesOneFramePerCallAndLoops()
    {
        var path = Path.Combine(Path.GetTempPath(), $"darkmount-test-{Guid.NewGuid():N}.gif");
        File.WriteAllBytes(path, TinyGif.Create(16, 16, [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255)]));
        try
        {
            using var screen = new AnimationScreen(AnimationSources.Create(AnimationKind.Gif, path));
            var c1 = CenterPixel(screen);
            var c2 = CenterPixel(screen);
            var c3 = CenterPixel(screen);
            var c4 = CenterPixel(screen);
            Assert.True(c1.Red > 200 && c1.Green < 50, $"frame 1 {c1}");
            Assert.True(c2.Green > 200 && c2.Red < 50, $"frame 2 {c2}");
            Assert.True(c3.Blue > 200 && c3.Red < 50, $"frame 3 {c3}");
            Assert.Equal(c1, c4); // looped
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Folder_ShowsImagesAlphabetically()
    {
        var dir = Directory.CreateTempSubdirectory("darkmount-test-");
        try
        {
            WriteSolidPng(Path.Combine(dir.FullName, "b.png"), SKColors.Lime);
            WriteSolidPng(Path.Combine(dir.FullName, "a.png"), SKColors.Red);
            File.WriteAllText(Path.Combine(dir.FullName, "notes.txt"), "ignored");
            using var screen = new AnimationScreen(AnimationSources.Create(AnimationKind.Folder, dir.FullName));
            Assert.Equal(SKColors.Red, CenterPixel(screen));
            Assert.Equal(SKColors.Lime, CenterPixel(screen));
            Assert.Equal(SKColors.Red, CenterPixel(screen));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Theory]
    [InlineData(AnimationKind.Gif, @"C:\definitely\missing\file.gif")]
    [InlineData(AnimationKind.Video, @"C:\definitely\missing\file.mp4")]
    [InlineData(AnimationKind.Folder, @"C:\definitely\missing\folder")]
    [InlineData(AnimationKind.Gif, null)]
    [InlineData(AnimationKind.Video, "")]
    public void MissingPath_FallsBackToPlasmaWithCaption(AnimationKind kind, string? path)
    {
        var source = AnimationSources.Create(kind, path);
        var plasma = Assert.IsType<PlasmaSource>(source);
        Assert.False(string.IsNullOrEmpty(plasma.Caption));
        using var screen = new AnimationScreen(source);
        string? save = kind == AnimationKind.Gif && path != null ? "anim-missing-gif" : null;
        Assert.NotEqual(Frame(screen, save), Frame(screen));
    }

    [Fact]
    public void Gif_CorruptFile_FallsBack()
    {
        var path = Path.Combine(Path.GetTempPath(), $"darkmount-test-{Guid.NewGuid():N}.gif");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        try
        {
            using var source = AnimationSources.Create(AnimationKind.Gif, path);
            Assert.IsType<PlasmaSource>(source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Video_WhenSampleAvailable_FramesAdvance()
    {
        // Uses a video that ships with Windows 11; passes trivially when it is absent.
        const string sample = @"C:\Windows\SystemApps\Microsoft.Windows.CloudExperienceHost_cw5n1h2txyewy\media\oobe-intro.mp4";
        if (!File.Exists(sample)) return;
        using var source = AnimationSources.Create(AnimationKind.Video, sample);
        Assert.IsType<VideoSource>(source);
        using var screen = new AnimationScreen(source);
        // The intro fades in from black, so look at frames a few seconds in.
        var frames = new List<byte[]>();
        for (int i = 0; i < 6; i++)
        {
            using var bmp = DockRenderer.Render(screen, Ctx);
            Assert.Equal(320, bmp.Width);
            if (i >= 4) ScreenRenderTests.Save(bmp, $"anim-video-{i - 3}");
            frames.Add(bmp.Bytes);
        }
        Assert.True(frames.Distinct(new BytesComparer()).Count() >= 3, "video frames do not advance");
    }

    private sealed class BytesComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) => obj.Length;
    }

    private static void WriteSolidPng(string path, SKColor color)
    {
        using var bmp = new SKBitmap(40, 30);
        bmp.Erase(color);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}

/// <summary>
/// Minimal GIF89a encoder for tests: one solid-colour frame per palette entry (max 4). The LZW stream emits a
/// clear code before every two literals so the code size never grows past 3 bits.
/// </summary>
internal static class TinyGif
{
    public static byte[] Create(int width, int height, IReadOnlyList<SKColor> frameColors)
    {
        using var ms = new MemoryStream();
        void W(params byte[] b) => ms.Write(b);
        void W16(int v) => W((byte)v, (byte)(v >> 8));

        W("GIF89a"u8.ToArray());
        W16(width);
        W16(height);
        W(0xF1, 0, 0); // global colour table with 4 entries
        for (int i = 0; i < 4; i++)
        {
            var c = i < frameColors.Count ? frameColors[i] : SKColors.Black;
            W(c.Red, c.Green, c.Blue);
        }
        // NETSCAPE2.0: loop forever
        W(0x21, 0xFF, 0x0B);
        W("NETSCAPE2.0"u8.ToArray());
        W(0x03, 0x01, 0x00, 0x00, 0x00);

        for (int f = 0; f < frameColors.Count; f++)
        {
            W(0x21, 0xF9, 0x04, 0x04, 10, 0, 0, 0); // GCE: disposal "do not dispose", 100 ms
            W(0x2C);
            W16(0); W16(0); W16(width); W16(height);
            W(0x00);
            W(0x02);                                 // LZW minimum code size
            var data = Lzw((byte)f, width * height);
            for (int i = 0; i < data.Length; i += 255)
            {
                int n = Math.Min(255, data.Length - i);
                W((byte)n);
                ms.Write(data, i, n);
            }
            W(0x00);
        }
        W(0x3B);
        return ms.ToArray();
    }

    private static byte[] Lzw(byte index, int pixels)
    {
        const int clear = 4, eoi = 5, bits = 3;
        var output = new List<byte>();
        int acc = 0, nbits = 0;
        void Emit(int code)
        {
            acc |= code << nbits;
            nbits += bits;
            while (nbits >= 8)
            {
                output.Add((byte)acc);
                acc >>= 8;
                nbits -= 8;
            }
        }
        for (int i = 0; i < pixels; i++)
        {
            if (i % 2 == 0) Emit(clear);
            Emit(index);
        }
        Emit(eoi);
        if (nbits > 0) output.Add((byte)acc);
        return output.ToArray();
    }
}
