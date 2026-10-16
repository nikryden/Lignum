using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

public class LineSplittingTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task<long> IndexedLineCount(string content)
    {
        using var doc = LogDocument.Open(_temp.WriteFile(content));
        await doc.StartIndexing().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(IndexingState.Completed, doc.IndexingState);
        return doc.LineCount;
    }

    [Fact]
    public async Task Empty_file_has_no_lines() =>
        Assert.Equal(0, await IndexedLineCount(""));

    [Theory]
    [InlineData("one\ntwo\nthree\n", 3)]
    [InlineData("one\r\ntwo\r\nthree\r\n", 3)]
    [InlineData("one\ntwo\r\nthree\n", 3)]
    public async Task Newline_and_crlf_end_lines(string content, long expected) =>
        Assert.Equal(expected, await IndexedLineCount(content));

    [Theory]
    [InlineData("one\rtwo\rthree\n", 1)]
    [InlineData("one\r", 1)]
    [InlineData("a\rb\nc\rd", 2)]
    public async Task Lone_carriage_return_is_not_a_line_break(string content, long expected) =>
        Assert.Equal(expected, await IndexedLineCount(content));

    [Theory]
    [InlineData("one\ntwo", 2)]
    [InlineData("single line, no newline", 1)]
    [InlineData("x", 1)]
    public async Task Text_after_the_last_newline_is_a_line(string content, long expected) =>
        Assert.Equal(expected, await IndexedLineCount(content));

    [Theory]
    [InlineData("\n", 1)]
    [InlineData("\n\n\n", 3)]
    [InlineData("a\n\n\nb\n", 4)]
    [InlineData("a\r\n\r\n\r\nb", 4)]
    public async Task Consecutive_newlines_make_empty_lines(string content, long expected) =>
        Assert.Equal(expected, await IndexedLineCount(content));

    [Fact]
    public async Task Lines_spanning_several_megabytes_are_all_counted()
    {
        // Line lengths vary so newlines fall at many different offsets, including block edges.
        var builder = new System.Text.StringBuilder();
        var lines = 0;
        while (builder.Length < 5_000_000)
        {
            builder.Append('x', lines % 997).Append(lines % 3 == 0 ? "\r\n" : "\n");
            lines++;
        }

        Assert.Equal(lines, await IndexedLineCount(builder.ToString()));
    }

    [Fact]
    public async Task A_single_very_long_line_is_one_line() =>
        Assert.Equal(1, await IndexedLineCount(new string('y', 3_000_000)));
}
