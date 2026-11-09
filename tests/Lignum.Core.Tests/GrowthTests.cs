using System.Text;
using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

/// <summary>After the file grows, only the appended bytes are indexed (tail mode).</summary>
public class GrowthTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static void Append(string path, byte[] bytes)
    {
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.Write(bytes);
    }

    private static void Append(string path, string text) => Append(path, Encoding.UTF8.GetBytes(text));

    private static string[] AllLines(LogDocument doc) => doc.ReadLines(0, (int)doc.LineCount);

    [Fact]
    public async Task Appended_lines_are_indexed_and_readable()
    {
        var path = _temp.WriteFile("one\ntwo\n");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();

        Append(path, "three\nfour\n");
        var added = await doc.IndexAppendedAsync();

        Assert.Equal(2, added);
        Assert.Equal(["one", "two", "three", "four"], AllLines(doc));
        Assert.Equal(new FileInfo(path).Length, doc.TotalBytes);
        Assert.Equal(doc.TotalBytes, doc.BytesIndexed);
    }

    [Fact]
    public async Task A_last_line_without_a_line_break_is_finished_by_the_growth()
    {
        var path = _temp.WriteFile("one\ntw");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();
        Assert.Equal(["one", "tw"], AllLines(doc));

        Append(path, "o\nthree");
        var added = await doc.IndexAppendedAsync();

        Assert.Equal(1, added);
        Assert.Equal(["one", "two", "three"], AllLines(doc));
    }

    [Fact]
    public async Task A_crlf_split_by_the_growth_ends_the_line_once()
    {
        var path = _temp.WriteFile("one\r");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();

        Append(path, "\ntwo\r\n");
        await doc.IndexAppendedAsync();

        Assert.Equal(["one", "two"], AllLines(doc));
    }

    [Fact]
    public async Task Growth_of_an_empty_file_starts_line_zero()
    {
        var path = _temp.WriteFile("");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();
        Assert.Equal(0, doc.LineCount);

        Append(path, "first\n");
        await doc.IndexAppendedAsync();

        Assert.Equal(["first"], AllLines(doc));
    }

    [Fact]
    public async Task A_utf16_code_unit_split_by_the_growth_is_read_whole()
    {
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var initial = utf16.GetPreamble().Concat(utf16.GetBytes("one\nt")).ToArray();
        var rest = utf16.GetBytes("wo\n");
        // Cut the file in the middle of the 't' code unit.
        var path = _temp.WriteBytes(initial[..^1]);
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();

        Append(path, [initial[^1], .. rest]);
        await doc.IndexAppendedAsync();

        Assert.Equal(["one", "two"], AllLines(doc));
    }

    [Fact]
    public async Task Without_growth_nothing_is_added()
    {
        var path = _temp.WriteFile("one\n");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();

        Assert.Equal(0, await doc.IndexAppendedAsync());
        Assert.Equal(["one"], AllLines(doc));
    }

    [Fact]
    public async Task Growth_during_the_indexing_pass_is_indexed_after_it()
    {
        var path = _temp.WriteFile("one\n");
        using var doc = LogDocument.Open(path);
        Append(path, "two\n"); // after opening: beyond the length the pass runs to
        var growth = doc.IndexAppendedAsync(); // requested before the pass has even started

        await doc.StartIndexing();
        Assert.Equal(1, await growth);
        Assert.Equal(["one", "two"], AllLines(doc));
    }

    [Fact]
    public async Task Several_requests_in_a_row_index_each_line_once()
    {
        var path = _temp.WriteFile("0\n");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();

        var requests = new List<Task<long>>();
        for (var i = 1; i <= 5; i++)
        {
            Append(path, $"{i}\n");
            requests.Add(doc.IndexAppendedAsync());
        }

        await Task.WhenAll(requests);
        Assert.Equal(["0", "1", "2", "3", "4", "5"], AllLines(doc));
    }

    [Fact]
    public async Task Growth_raises_lines_added_with_the_new_count()
    {
        var path = _temp.WriteFile("one\n");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();
        LinesAddedEventArgs? last = null;
        doc.LinesAdded += (_, e) => last = e;

        Append(path, "two\n");
        await doc.IndexAppendedAsync();

        Assert.NotNull(last);
        Assert.Equal(2, last.LineCount);
        Assert.Equal(8, last.BytesIndexed);
    }

    [Fact]
    public async Task Nothing_is_indexed_once_the_index_is_invalid()
    {
        var path = _temp.WriteFile("one\ntwo\n");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();

        File.WriteAllText(path, "x\n");
        doc.CheckForChanges(); // truncated: the index is invalid
        Append(path, "more\nlines\nhere\n");

        Assert.Equal(0, await doc.IndexAppendedAsync());
        Assert.Equal(0, doc.LineCount);
    }

    [Fact]
    public async Task After_dispose_a_request_does_nothing()
    {
        var path = _temp.WriteFile("one\n");
        var doc = LogDocument.Open(path);
        await doc.StartIndexing();
        doc.Dispose();
        Append(path, "two\n");

        Assert.Equal(0, await doc.IndexAppendedAsync());
    }

    [Fact]
    public async Task A_file_rewritten_in_place_and_longer_is_not_taken_as_an_append()
    {
        var path = _temp.WriteFile("[1] first line\n[2] second line\n");
        using var doc = LogDocument.Open(path);
        var changes = new List<FileChangeKind>();
        doc.FileChanged += (_, e) => { lock (changes) changes.Add(e.Kind); };
        await doc.StartIndexing();

        // Like an editor saving: same file, new content, longer than before, text shifted.
        using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            writer.Write(Encoding.UTF8.GetBytes("abc[1] first line\n[2] second line\n[3] third\n"));

        Assert.Equal(0, await doc.IndexAppendedAsync());
        Assert.False(doc.IsIndexValid); // the tab must reload instead of showing shifted lines
        Assert.Equal(0, doc.LineCount);
        lock (changes) Assert.Contains(FileChangeKind.Replaced, changes);
    }
}
