using System.IO.MemoryMappedFiles;
using System.Text;

namespace Darkmount.Sensors;

/// <summary>Read-only snapshot access to named shared memory published by monitoring tools.</summary>
public static class SharedMemory
{
    /// <summary>Copies the whole view of the named mapping, or returns null when it does not exist or cannot be read.</summary>
    public static byte[]? TryRead(string name)
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            using var view = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            var buffer = new byte[view.Length];
            view.ReadExactly(buffer);
            return buffer;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Decodes a zero-terminated single-byte (ANSI/Latin-1) string stored in a fixed-size field.</summary>
    internal static string ReadString(ReadOnlySpan<byte> blob, int offset, int maxLength)
    {
        if (offset < 0 || offset >= blob.Length) return "";
        var field = blob.Slice(offset, Math.Min(maxLength, blob.Length - offset));
        int end = field.IndexOf((byte)0);
        if (end >= 0) field = field[..end];
        return Encoding.Latin1.GetString(field).Trim();
    }
}
