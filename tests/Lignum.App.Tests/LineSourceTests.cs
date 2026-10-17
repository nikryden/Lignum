using Lignum.App.Settings;
using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.Tests;

public class LineSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<LogDocument> _documents = [];

    public LineSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var doc in _documents) doc.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task<LogDocument> OpenIndexed(string content)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(path, content);
        var doc = LogDocument.Open(path);
        _documents.Add(doc);
        await doc.StartIndexing().WaitAsync(TimeSpan.FromSeconds(10));
        return doc;
    }

    private static string Lines(int n) => string.Concat(Enumerable.Range(0, n).Select(i => $"line {i}\n"));

    [Fact]
    public async Task A_document_source_has_one_row_per_line()
    {
        var source = LineSource.ForDocument(await OpenIndexed(Lines(10)));

        Assert.Equal(10, source.RowCount);
        var rows = await source.ReadRowsAsync(3, 2);

        Assert.Equal([new DisplayRow(3, "line 3"), new DisplayRow(4, "line 4")], rows);
    }

    [Fact]
    public async Task Rows_past_the_end_are_not_returned()
    {
        var source = LineSource.ForDocument(await OpenIndexed(Lines(5)));

        var rows = await source.ReadRowsAsync(3, 50);

        Assert.Equal([3L, 4L], rows.Select(r => r.LineNumber));
        Assert.Empty(await source.ReadRowsAsync(9, 5));
    }

    [Fact]
    public async Task A_source_over_selected_line_numbers_shows_only_those_lines()
    {
        // The results pane: rows map to arbitrary, non-contiguous line numbers.
        var doc = await OpenIndexed(Lines(20));
        var source = new LineSource(new FixedLineList([2, 3, 4, 9, 17]), new DocumentLineReader(doc));

        Assert.Equal(5, source.RowCount);
        var rows = await source.ReadRowsAsync(1, 3);

        Assert.Equal(
            [new DisplayRow(3, "line 3"), new DisplayRow(4, "line 4"), new DisplayRow(9, "line 9")], rows);
    }

    [Fact]
    public async Task A_document_source_reports_lines_added_while_indexing()
    {
        var path = Path.Combine(_dir, "growing.log");
        File.WriteAllText(path, string.Concat(Enumerable.Range(0, 300_000).Select(i => $"line {i}\n")));
        using var doc = LogDocument.Open(path);
        var source = LineSource.ForDocument(doc);
        var changes = 0;
        source.Changed += (_, _) => Interlocked.Increment(ref changes);

        await doc.StartIndexing().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(changes > 0);
        Assert.Equal(300_000, source.RowCount);
    }

    private sealed class FixedLineList(long[] lines) : ILineList
    {
        public long Count => lines.Length;
        public long this[long row] => lines[row];
        public event EventHandler? Changed { add { } remove { } }
    }
}
