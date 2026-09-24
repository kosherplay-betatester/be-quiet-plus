namespace Darkmount.Screens;

/// <summary>Creates frame sources. Never throws: a missing or unreadable path yields a Plasma source with an error caption.</summary>
public static class AnimationSources
{
    public static IFrameSource Create(AnimationKind kind, string? path)
    {
        switch (kind)
        {
            case AnimationKind.Plasma: return new PlasmaSource();
            case AnimationKind.Matrix: return new MatrixSource();
            case AnimationKind.Starfield: return new StarfieldSource();
        }

        string what = kind switch
        {
            AnimationKind.Gif => "GIF",
            AnimationKind.Video => "Video",
            AnimationKind.Folder => "Folder",
            _ => kind.ToString(),
        };
        if (string.IsNullOrWhiteSpace(path)) return new PlasmaSource($"{what}: no file selected");

        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        try
        {
            return kind switch
            {
                AnimationKind.Gif => new GifSource(path),
                AnimationKind.Video => new VideoSource(path),
                AnimationKind.Folder => new FolderSource(path),
                _ => new PlasmaSource($"Unknown animation '{kind}'"),
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new PlasmaSource(kind == AnimationKind.Folder && Directory.Exists(path)
                ? $"{what}: no images in {name}"
                : $"{what} not found: {name}");
        }
        catch (Exception)
        {
            return new PlasmaSource($"{what}: cannot play {name}");
        }
    }
}
