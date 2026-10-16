using Lignum.Core;

namespace Lignum.App.Viewer;

/// <summary>
/// The ordered file line numbers a viewer shows, one per row: every line of a document, or a
/// result set (find all / filter). It may grow while indexing or a search runs.
/// </summary>
public interface ILineList
{
    /// <summary>Number of rows. Thread-safe.</summary>
    long Count { get; }

    /// <summary>The 0-based file line number shown in <paramref name="row"/>. Thread-safe.</summary>
    long this[long row] { get; }

    /// <summary>Raised, possibly on a background thread, when <see cref="Count"/> changes.</summary>
    event EventHandler? Changed;
}

/// <summary>Reads line text by file line number. Does file I/O: never call it on the UI thread.</summary>
public interface ILineReader
{
    /// <summary>Text of up to <paramref name="count"/> consecutive lines, clamped to the lines available.</summary>
    string[] ReadLines(long firstLine, int count);
}

/// <summary>One row ready to draw: its 0-based file line number and text (without the line break).</summary>
public sealed record DisplayRow(long LineNumber, string Text);

/// <summary>
/// What a <see cref="LogView"/> displays: a list of line numbers plus the reader that supplies their
/// text. The same control shows the whole file or a result set by swapping the list.
/// </summary>
public sealed class LineSource(ILineList lines, ILineReader reader)
{
    /// <summary>A source showing every line of <paramref name="document"/>, growing as it is indexed.</summary>
    public static LineSource ForDocument(LogDocument document) =>
        new(new AllLines(document), new DocumentLineReader(document));

    public ILineList Lines { get; } = lines;

    public ILineReader Reader { get; } = reader;

    /// <summary>Number of rows. Thread-safe.</summary>
    public long RowCount => Lines.Count;

    /// <summary>Raised, possibly on a background thread, when <see cref="RowCount"/> changes.</summary>
    public event EventHandler? Changed
    {
        add => Lines.Changed += value;
        remove => Lines.Changed -= value;
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> rows from <paramref name="firstRow"/> on a background thread.
    /// Rows past the end are left out. Consecutive line numbers are read in one call.
    /// </summary>
    public Task<IReadOnlyList<DisplayRow>> ReadRowsAsync(long firstRow, int count,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadRows(firstRow, count, cancellationToken), cancellationToken);

    private IReadOnlyList<DisplayRow> ReadRows(long firstRow, int count, CancellationToken cancellationToken)
    {
        var available = (int)Math.Clamp(Lines.Count - firstRow, 0, count);
        var rows = new List<DisplayRow>(available);
        var row = firstRow;
        var end = firstRow + available;
        while (row < end)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Gather a run of consecutive line numbers.
            var firstLine = Lines[row];
            var runLength = 1;
            while (row + runLength < end && Lines[row + runLength] == firstLine + runLength) runLength++;

            var texts = Reader.ReadLines(firstLine, runLength);
            for (var i = 0; i < texts.Length; i++) rows.Add(new DisplayRow(firstLine + i, texts[i]));
            if (texts.Length < runLength) break; // the lines are no longer there
            row += runLength;
        }

        return rows;
    }
}

/// <summary>Reads lines from a <see cref="LogDocument"/>.</summary>
public sealed class DocumentLineReader(LogDocument document) : ILineReader
{
    public string[] ReadLines(long firstLine, int count) => document.ReadLines(firstLine, count);
}

/// <summary>Every line of a document, in order; grows while it is indexed.</summary>
internal sealed class AllLines : ILineList
{
    private readonly LogDocument _document;

    public AllLines(LogDocument document)
    {
        _document = document;
        document.LinesAdded += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public long Count => _document.LineCount;

    public long this[long row] => row;

    public event EventHandler? Changed;
}
