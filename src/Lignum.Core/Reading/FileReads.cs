using Microsoft.Win32.SafeHandles;

namespace Lignum.Core.Reading;

internal static class FileReads
{
    /// <summary>
    /// Reads until <paramref name="destination"/> is full or the file ends, and returns the bytes read
    /// (fewer only when the file is shorter, e.g. it shrank; change detection deals with that).
    /// </summary>
    public static int ReadFully(SafeFileHandle handle, Span<byte> destination, long offset)
    {
        var filled = 0;
        while (filled < destination.Length)
        {
            var read = RandomAccess.Read(handle, destination[filled..], offset + filled);
            if (read == 0) break;
            filled += read;
        }

        return filled;
    }
}
