using System.Text;
using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

public class TextRangeReadingTests : IDisposable
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

    private static string Read(LogDocument doc, long startLine, int startColumn, long endLine, int endColumn)
    {
        var writer = new StringWriter();
        doc.ReadTextRange(new FilePosition(startLine, startColumn), new FilePosition(endLine, endColumn), writer);
        return writer.ToString();
    }

    [Theory]
    [InlineData(2, 5, "llo")]
    [InlineData(0, 11, "hello world")]
    [InlineData(4, 4, "")]
    public async Task Reads_part_of_one_line(int startColumn, int endColumn, string expected)
    {
        using var doc = await OpenIndexed("hello world\nsecond\n");

        Assert.Equal(expected, Read(doc, 0, startColumn, 0, endColumn));
    }

    [Fact]
    public async Task A_multi_line_range_keeps_the_original_line_breaks()
    {
        using var doc = await OpenIndexed("alpha\r\nbeta\ngamma\r\n\r\ndelta\n");

        Assert.Equal("pha\r\nbeta\ngamma\r\n\r\nde", Read(doc, 0, 2, 4, 2));
        Assert.Equal("\nga", Read(doc, 1, 4, 2, 2));
        Assert.Equal("a\r\n", Read(doc, 0, 4, 1, 0));
    }

    [Fact]
    public async Task Columns_past_the_end_of_a_line_mean_the_end_of_that_line()
    {
        using var doc = await OpenIndexed("one\r\ntwo\nthree");

        Assert.Equal("one\r\ntwo\nthree", Read(doc, 0, 0, 2, int.MaxValue));
        Assert.Equal("\r\ntw", Read(doc, 0, 99, 1, 2));
        Assert.Equal("", Read(doc, 1, 50, 1, int.MaxValue));
    }

    [Fact]
    public async Task A_range_across_many_blocks_reads_back_exactly()
    {
        // Long ranges cross 64 KB cache blocks and 1 MB streaming chunks; multi-byte characters and "\r\n"
        // pairs land on every kind of edge.
        var builder = new StringBuilder();
        var lines = 0;
        for (var i = 0; builder.Length < 3_000_000; i++, lines++)
        {
            builder.Append(i).Append(':').Append(new string((char)('a' + i % 26), i % 997))
                .Append(i % 5 == 0 ? "é✓日本" : "").Append(i % 3 == 0 ? "\r\n" : "\n");
        }

        var content = builder.ToString();
        using var doc = await OpenIndexed(content);

        Assert.Equal(content.TrimEnd('\r', '\n'), Read(doc, 0, 0, lines - 1, int.MaxValue));
        var secondLineStart = content.IndexOf('\n') + 1;
        var lastLineStart = content.LastIndexOf('\n', content.Length - 2) + 1;
        Assert.Equal(content[(secondLineStart + 1)..(lastLineStart + 3)], Read(doc, 1, 1, lines - 1, 3));
    }

    [Fact]
    public async Task Rejects_lines_that_are_not_indexed_and_reversed_ranges()
    {
        using var doc = await OpenIndexed("a\nb\n");

        Assert.Throws<ArgumentOutOfRangeException>(() => Read(doc, 0, 0, 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(doc, 1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(doc, 0, -1, 1, 0));
    }

    [Fact]
    public async Task A_cancelled_read_stops()
    {
        using var doc = await OpenIndexed(string.Concat(Enumerable.Repeat("line\n", 1000)));

        Assert.Throws<OperationCanceledException>(() =>
            doc.ReadTextRange(new FilePosition(0, 0), new FilePosition(999, 4), new StringWriter(),
                new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Measuring_a_range_gives_its_size_in_bytes_without_reading_it()
    {
        using var doc = await OpenIndexed("0123456789\r\nabcdefghij\nxyz");

        Assert.Equal(5, doc.MeasureTextRange(new FilePosition(0, 2), new FilePosition(0, 7)));
        Assert.Equal(12 + 11 + 3, doc.MeasureTextRange(new FilePosition(0, 0), new FilePosition(2, int.MaxValue)));
        Assert.Equal(12 + 11 + 3, doc.MeasureTextRange(new FilePosition(2, int.MaxValue), new FilePosition(0, 0)));
    }
}
