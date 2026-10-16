namespace Lignum.Core;

/// <summary>
/// The start of a line, at most a given number of characters, and whether the line goes on past it.
/// Used to display huge lines (a minified JSON dump) without decoding megabytes per row.
/// </summary>
public readonly record struct LinePrefix(string Text, bool IsTruncated);

// Reading only the start of a line: display truncation. Search and full-text reads still see the whole line.
public sealed partial class LogDocument
{
    /// <summary>
    /// Returns the first <paramref name="maxChars"/> characters of line <paramref name="line"/> (0-based) and
    /// whether the line is longer. Reads only the bytes needed, so a multi-megabyte line costs no more than the
    /// limit. A surrogate pair is never split. Thread-safe; does file I/O, so call it off the UI thread.
    /// </summary>
    public LinePrefix ReadLinePrefix(long line, int maxChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxChars);
        var (start, end) = LineBounds(line);
        // Enough bytes for maxChars characters plus one more, so "longer than the limit" is decidable.
        var limitBytes = MaxBytesFor(maxChars + 1);
        var length = (int)Math.Min(end - start, limitBytes);
        var buffer = new byte[length];
        var read = Blocks.Read(start, buffer);
        var text = Decode(buffer.AsSpan(0, read));

        var cutBytes = end - start > length;
        if (text.Length <= maxChars && !cutBytes) return new LinePrefix(text, false);

        var keep = Math.Min(maxChars, text.Length);
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1])) keep--;
        return new LinePrefix(text[..keep], true);
    }

    /// <summary>
    /// <see cref="ReadLinePrefix"/> for up to <paramref name="count"/> lines from <paramref name="firstLine"/>,
    /// clamped to the lines indexed so far. Returns nothing if the file was truncated or replaced meanwhile.
    /// </summary>
    public LinePrefix[] ReadLinePrefixes(long firstLine, int count, int maxChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstLine);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var available = (int)Math.Clamp(LineCount - firstLine, 0, count);
        var lines = new LinePrefix[available];
        for (var i = 0; i < available; i++) lines[i] = ReadLinePrefix(firstLine + i, maxChars);
        return IsIndexValid ? lines : [];
    }

    /// <summary>Most bytes <paramref name="chars"/> UTF-16 characters can take in the file's encoding.</summary>
    private static long MaxBytesFor(int chars) => 3L * chars + 3; // UTF-8: at most 3 bytes per UTF-16 char
}
