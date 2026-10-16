namespace Lignum.Core;

// Reading text ranges (selection copy): streamed from the file in chunks, never the whole range at once.
public sealed partial class LogDocument
{
    private const int RangeChunkSize = 1024 * 1024;

    /// <summary>
    /// Writes the text between <paramref name="start"/> (inclusive) and <paramref name="end"/> (exclusive) to
    /// <paramref name="output"/>, keeping the file's own line breaks (<c>\n</c> or <c>\r\n</c>) between lines.
    /// Columns past the end of their line are clamped to it, so <c>int.MaxValue</c> means "end of line".
    /// The lines between the first and last are streamed in chunks, so any range size works. Thread-safe;
    /// does file I/O, so call it off the UI thread.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A line is not indexed, a column is negative, or end is before start.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; part of the text may have been written.</exception>
    public void ReadTextRange(FilePosition start, FilePosition end, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegative(start.Column, nameof(start));
        ArgumentOutOfRangeException.ThrowIfNegative(end.Column, nameof(end));
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end), "The end is before the start.");

        if (start.Line == end.Line)
        {
            var text = ReadLine(start.Line);
            var from = Math.Min(start.Column, text.Length);
            output.Write(text.AsSpan(from, Math.Min(end.Column, text.Length) - from));
            return;
        }

        var first = ReadLine(start.Line);
        var lastBounds = LineBounds(end.Line); // validates end.Line before anything is written
        output.Write(first.AsSpan(Math.Min(start.Column, first.Length)));

        // From the end of the first line's text (its line break) to the start of the last line, as stored.
        StreamBytes(LineBounds(start.Line).End, lastBounds.Start, output, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var last = ReadLine(end.Line);
        output.Write(last.AsSpan(0, Math.Min(end.Column, last.Length)));
    }

    /// <summary>
    /// Approximate size in bytes of the text between two positions, from the line index alone (no I/O), so
    /// the UI can decide whether a copy is large before reading anything. Columns count as one byte each.
    /// </summary>
    public long MeasureTextRange(FilePosition start, FilePosition end)
    {
        if (end < start) (start, end) = (end, start);
        var count = LineCount;
        if (count == 0 || start.Line >= count) return 0;
        var endLine = Math.Min(end.Line, count - 1);
        return Math.Max(0, OffsetOf(endLine, end.Column) - OffsetOf(start.Line, start.Column));

        long OffsetOf(long line, int column)
        {
            var lineStart = _index[line];
            var lineLimit = line + 1 < count ? _index[line + 1] : Math.Max(lineStart, BytesIndexed);
            return Math.Min(lineStart + Math.Max(0, column), lineLimit);
        }
    }

    private void StreamBytes(long from, long to, TextWriter output, CancellationToken cancellationToken)
    {
        if (to <= from) return;
        var decoder = CreateDecoder();
        var bytes = new byte[(int)Math.Min(RangeChunkSize, to - from)];
        // Room for a full chunk plus characters left over from the previous chunk's split sequence.
        var chars = new char[TextEncoding.GetMaxCharCount(bytes.Length + 8)];

        var position = from;
        while (position < to)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wanted = (int)Math.Min(bytes.Length, to - position);
            var read = RandomAccess.Read(_handle, bytes.AsSpan(0, wanted), position);
            if (read == 0) break; // the file shrank; change detection reports it
            position += read;
            var count = decoder.GetChars(bytes, 0, read, chars, 0, flush: position >= to);
            output.Write(chars, 0, count);
        }

        // The file may have been truncated or replaced while we read: the text could be stale.
        if (!IsIndexValid) throw new IOException("The file was truncated or replaced while it was read.");
    }
}
