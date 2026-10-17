using System.Text;
using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

public class LineReadingTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task<LogDocument> OpenIndexed(string content)
    {
        var doc = LogDocument.Open(_temp.WriteFile(content));
        await doc.StartIndexing().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(IndexingState.Completed, doc.IndexingState);
        return doc;
    }

    [Theory]
    [InlineData(0, "first")]
    [InlineData(1, "second")]
    [InlineData(2, "third")]
    public async Task Reads_a_single_line_without_its_line_break(long line, string expected)
    {
        using var doc = await OpenIndexed("first\nsecond\r\nthird\n");

        Assert.Equal(expected, doc.ReadLine(line));
    }

    [Theory]
    [InlineData(0, 3, new[] { "l0", "l1", "l2" })]          // start of file
    [InlineData(4, 3, new[] { "l4", "l5", "l6" })]          // middle
    [InlineData(7, 3, new[] { "l7", "l8", "l9" })]          // end of file
    [InlineData(8, 10, new[] { "l8", "l9" })]               // past the end: clamped to the lines that exist
    public async Task Reads_a_range_of_lines(long first, int count, string[] expected)
    {
        using var doc = await OpenIndexed(string.Concat(Enumerable.Range(0, 10).Select(i => $"l{i}\n")));

        Assert.Equal(expected, doc.ReadLines(first, count));
    }

    [Theory]
    [InlineData("a\n\nb", new[] { "a", "", "b" })]
    [InlineData("no newline at end", new[] { "no newline at end" })]
    [InlineData("crlf\r\n\r\n", new[] { "crlf", "" })]
    [InlineData("lone\rcr\n", new[] { "lone\rcr" })]
    [InlineData("ends with cr\r", new[] { "ends with cr\r" })]
    [InlineData("héllo wörld ✓\n日本語", new[] { "héllo wörld ✓", "日本語" })]
    public async Task Line_text_keeps_everything_but_the_line_break(string content, string[] expected)
    {
        using var doc = await OpenIndexed(content);

        Assert.Equal(expected, doc.ReadLines(0, 100));
    }

    [Fact]
    public async Task Lines_spanning_block_boundaries_read_back_exactly()
    {
        // Varying lengths and multi-byte characters put line breaks, "\r\n" pairs and characters
        // across every kind of 64 KB block edge.
        var expected = new List<string>();
        var builder = new StringBuilder();
        for (var i = 0; builder.Length < 600_000; i++)
        {
            var line = $"{i}:" + new string((char)('a' + i % 26), i % 1500) + (i % 7 == 0 ? "é✓" : "");
            expected.Add(line);
            builder.Append(line).Append(i % 3 == 0 ? "\r\n" : "\n");
        }

        using var doc = await OpenIndexed(builder.ToString());

        Assert.Equal(expected.Count, doc.LineCount);
        Assert.Equal(expected, doc.ReadLines(0, expected.Count));
        // Random access in reverse order goes through the cache differently from a forward scan.
        for (var i = expected.Count - 1; i >= 0; i -= 37) Assert.Equal(expected[i], doc.ReadLine(i));
    }

    [Fact]
    public async Task A_line_larger_than_the_cache_is_returned_in_full()
    {
        var huge = new string('x', 9_000_000);
        using var doc = await OpenIndexed($"before\n{huge}\nafter\n");

        Assert.Equal(huge, doc.ReadLine(1));
        Assert.Equal(["before", huge, "after"], doc.ReadLines(0, 3));
    }

    [Fact]
    public async Task Reading_outside_the_indexed_lines_throws()
    {
        using var doc = await OpenIndexed("one\ntwo\n");

        Assert.Throws<ArgumentOutOfRangeException>(() => doc.ReadLine(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.ReadLine(-1));
        Assert.Empty(doc.ReadLines(5, 3));
    }

    [Fact]
    public async Task Nothing_is_read_once_the_file_has_been_truncated()
    {
        using var doc = await OpenIndexed("one\ntwo\nthree\n");
        Assert.Equal("two", doc.ReadLine(1));

        using (var writer = new FileStream(doc.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            writer.SetLength(4);
        doc.CheckForChanges();

        Assert.False(doc.IsIndexValid);
        Assert.Empty(doc.ReadLines(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.ReadLine(1));
    }

    [Fact]
    public async Task The_first_lines_are_readable_before_indexing_finishes()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 400_000; i++) builder.Append("line ").Append(i).Append('\n');
        using var doc = LogDocument.Open(_temp.WriteFile(builder.ToString()));
        var firstScreen = new TaskCompletionSource<string[]>();
        doc.LinesAdded += (_, e) =>
        {
            if (e.LineCount >= 50) firstScreen.TrySetResult(doc.ReadLines(0, 50));
        };

        var indexing = doc.StartIndexing();
        var lines = await firstScreen.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await indexing;

        Assert.Equal(Enumerable.Range(0, 50).Select(i => $"line {i}"), lines);
    }
}
