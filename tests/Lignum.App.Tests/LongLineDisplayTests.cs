using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.Tests;

public class LongLineDisplayTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<LogDocument> _documents = [];

    public LongLineDisplayTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var doc in _documents) doc.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<LogDocument> OpenIndexed(string content)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(path, content);
        var doc = LogDocument.Open(path);
        _documents.Add(doc);
        await doc.StartIndexing().WaitAsync(TimeSpan.FromSeconds(30));
        return doc;
    }

    [Fact]
    public async Task Rows_longer_than_the_display_limit_are_cut_and_flagged()
    {
        var huge = new string('x', 3_000_000);
        var source = LineSource.ForDocument(await OpenIndexed($"short\n{huge}\nend\n"));

        var rows = await source.ReadRowsAsync(0, 3);

        Assert.Equal(10_000, LineSource.DisplayLimit);
        Assert.Equal(
            [
                new DisplayRow(0, "short"),
                new DisplayRow(1, new string('x', 10_000), IsTruncated: true),
                new DisplayRow(2, "end"),
            ],
            rows);
    }

    [Fact]
    public async Task A_reader_without_a_prefix_read_is_truncated_the_same_way()
    {
        var source = new LineSource(new FixedLines(2), new InMemoryReader(["ok", new string('y', 10_001)]));

        var rows = await source.ReadRowsAsync(0, 2);

        Assert.Equal([new DisplayRow(0, "ok"), new DisplayRow(1, new string('y', 10_000), IsTruncated: true)], rows);
    }

    private sealed class FixedLines(long count) : ILineList
    {
        public long Count => count;
        public long this[long row] => row;
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class InMemoryReader(string[] lines) : ILineReader
    {
        public string[] ReadLines(long firstLine, int count) =>
            lines.Skip((int)firstLine).Take(count).ToArray();
    }
}
