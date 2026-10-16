using System.Text;
using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

public class LongLineTests : IDisposable
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

    private static string LongLine(int length) =>
        string.Concat(Enumerable.Range(0, length / 10 + 1).Select(i => $"{i % 100000:D9}|"))[..length];

    [Fact]
    public async Task The_full_text_of_a_multi_megabyte_line_is_returned_whatever_the_display_limit()
    {
        var huge = LongLine(5_000_000);
        using var doc = await OpenIndexed($"before\n{huge}\nafter\n");

        Assert.Equal(3, doc.LineCount);
        Assert.Equal(huge, doc.ReadLine(1));
        Assert.Equal(["before", huge, "after"], doc.ReadLines(0, 3));
    }

    [Fact]
    public async Task A_line_prefix_is_cut_at_the_limit_and_marked_truncated()
    {
        var huge = LongLine(5_000_000);
        using var doc = await OpenIndexed($"short\n{huge}\r\nafter");

        var prefixes = doc.ReadLinePrefixes(0, 3, maxChars: 10_000);

        Assert.Equal(
            [new LinePrefix("short", false), new LinePrefix(huge[..10_000], true), new LinePrefix("after", false)],
            prefixes);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public async Task A_line_exactly_at_the_limit_is_not_truncated(int length, bool truncated)
    {
        var line = LongLine(length);
        using var doc = await OpenIndexed(line + "\n");

        var prefix = doc.ReadLinePrefix(0, maxChars: 10);

        Assert.Equal(new LinePrefix(line[..Math.Min(10, length)], truncated), prefix);
    }

    [Fact]
    public async Task Multi_byte_characters_count_as_characters_not_bytes()
    {
        // 3-byte characters: the prefix holds whole characters, as many as the limit allows.
        var line = new string('日', 100);
        using var doc = await OpenIndexed(line);

        Assert.Equal(new LinePrefix(new string('日', 40), true), doc.ReadLinePrefix(0, maxChars: 40));
        Assert.Equal(new LinePrefix(line, false), doc.ReadLinePrefix(0, maxChars: 100));
    }

    [Fact]
    public async Task A_surrogate_pair_is_never_split_at_the_limit()
    {
        // "😀" is two UTF-16 chars; a limit landing between them drops the half character.
        using var doc = await OpenIndexed("ab😀cd");

        Assert.Equal(new LinePrefix("ab", true), doc.ReadLinePrefix(0, maxChars: 3));
        Assert.Equal(new LinePrefix("ab😀", true), doc.ReadLinePrefix(0, maxChars: 4));
    }

    [Fact]
    public async Task Prefixes_are_clamped_to_the_lines_that_exist()
    {
        using var doc = await OpenIndexed("a\nb\n");

        Assert.Equal([new LinePrefix("b", false)], doc.ReadLinePrefixes(1, 10, maxChars: 5));
        Assert.Empty(doc.ReadLinePrefixes(5, 10, maxChars: 5));
    }
}
