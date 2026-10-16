using System.Text;
using Lignum.Core.Reading;

namespace Lignum.Core;

// Reading lines: positional reads through a small LRU block cache, never the whole file.
public sealed partial class LogDocument
{
    private BlockCache? _blocks;

    private BlockCache Blocks => LazyInitializer.EnsureInitialized(ref _blocks, () => new BlockCache(_handle));

    /// <summary>
    /// Returns the full text of line <paramref name="line"/> (0-based) without its line break, however long
    /// it is. Lines are available as soon as they are indexed. Thread-safe; does file I/O, so call it off
    /// the UI thread.
    /// </summary>
    public string ReadLine(long line)
    {
        var (start, end) = LineBounds(line);
        var length = checked((int)(end - start));
        var buffer = new byte[length];
        var read = Blocks.Read(start, buffer);
        return Decode(buffer.AsSpan(0, read));
    }

    /// <summary>
    /// Returns the text of up to <paramref name="count"/> lines starting at <paramref name="firstLine"/>
    /// (0-based), without line breaks. The range is clamped to the lines indexed so far. Thread-safe; does
    /// file I/O, so call it off the UI thread.
    /// </summary>
    public string[] ReadLines(long firstLine, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstLine);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var available = (int)Math.Clamp(LineCount - firstLine, 0, count);
        var lines = new string[available];
        for (var i = 0; i < available; i++) lines[i] = ReadLine(firstLine + i);
        return lines;
    }

    /// <summary>
    /// Byte range of a line's text, excluding its <c>\n</c> or <c>\r\n</c>. The last indexed line ends at
    /// <see cref="BytesIndexed"/>, read before the line count so the range never runs past a line start
    /// the indexer has not published yet.
    /// </summary>
    private (long Start, long End) LineBounds(long line)
    {
        var indexedEnd = BytesIndexed;
        var count = _index.Count;
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, count);

        var start = _index[line];
        long end;
        if (line + 1 < count)
        {
            end = _index[line + 1] - 1; // drop the '\n' that precedes the next line
            if (end > start && ByteAt(end - 1) == (byte)'\r') end--;
        }
        else
        {
            end = Math.Max(start, indexedEnd);
            if (end > start && ByteAt(end - 1) == (byte)'\n')
            {
                end--;
                if (end > start && ByteAt(end - 1) == (byte)'\r') end--;
            }
        }

        return (start, end);
    }

    private byte ByteAt(long offset)
    {
        Span<byte> one = stackalloc byte[1];
        return Blocks.Read(offset, one) == 1 ? one[0] : (byte)0;
    }

    private static string Decode(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);
}
