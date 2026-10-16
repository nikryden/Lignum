using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

public class QueryTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task<LogDocument> OpenIndexed(string content)
    {
        var doc = LogDocument.Open(_temp.WriteFile(content));
        await doc.StartIndexing();
        return doc;
    }

    private static async Task<long[]> Run(LogDocument doc, Query query)
    {
        using var result = doc.StartQuery(query);
        await result.Completion;
        Assert.Equal(QueryState.Completed, result.State);
        return Lines(result);
    }

    private static long[] Lines(QueryResult result)
    {
        var lines = new long[result.Count];
        for (var i = 0; i < lines.Length; i++) lines[i] = result[i];
        return lines;
    }

    [Fact]
    public async Task Find_returns_the_matching_line_numbers_in_order()
    {
        using var doc = await OpenIndexed("alpha\nerror one\nbeta\nan error\nerror\n");

        var lines = await Run(doc, Query.Find("error"));

        Assert.Equal([1L, 3L, 4L], lines);
    }

    [Fact]
    public async Task Find_ignores_case_unless_asked_not_to()
    {
        using var doc = await OpenIndexed("ERROR\nerror\nErRoR\nnone\n");

        Assert.Equal(new long[] { 0L, 1L, 2L }, await Run(doc, Query.Find("error")));
        Assert.Equal(new long[] { 1L }, await Run(doc, Query.Find("error", matchCase: true)));
    }

    [Fact]
    public async Task Line_breaks_are_not_part_of_the_searched_text()
    {
        using var doc = await OpenIndexed("ends with x\r\nx\r\nlast x");

        Assert.Equal(new long[] { 0L, 1L, 2L }, await Run(doc, Query.Find("x$", isRegex: true)));
    }

    [Fact]
    public async Task A_match_far_inside_a_very_long_line_is_found()
    {
        var longLine = new string('a', 200_000) + "needle" + new string('b', 50_000);
        using var doc = await OpenIndexed($"short\n{longLine}\nshort\n");

        Assert.Equal(new long[] { 1L }, await Run(doc, Query.Find("needle")));
    }

    [Fact]
    public async Task Matches_across_many_parallel_chunks_come_back_in_file_order()
    {
        // About 12 MB: several 2 MB chunks and more than one batch on small machines.
        var sb = new System.Text.StringBuilder();
        var expected = new List<long>();
        for (var i = 0; i < 200_000; i++)
        {
            if (i % 997 == 0)
            {
                sb.Append("line ").Append(i).Append(" has the MARKER in it\n");
                expected.Add(i);
            }
            else
            {
                sb.Append("line ").Append(i).Append(" is just filler text of some length\n");
            }
        }

        using var doc = await OpenIndexed(sb.ToString());

        Assert.Equal(expected, await Run(doc, Query.Find("marker")));
    }

    [Fact]
    public async Task Progress_rises_to_one_while_matches_are_reported()
    {
        using var doc = LogDocument.Open(_temp.WriteFile("a\nb\na\n"));
        using var result = doc.StartQuery(Query.Find("a"));
        var progress = new List<double>();
        result.Changed += (_, _) => { lock (progress) progress.Add(result.Progress); };
        Assert.Equal(0.0, result.Progress); // nothing indexed yet, so nothing searched

        await doc.StartIndexing();
        await result.Completion;

        Assert.Equal(1.0, result.Progress);
        Assert.Equal(2, result.Count);
        lock (progress) Assert.Equal(1.0, progress[^1]);
    }

    [Fact]
    public async Task A_query_can_be_cancelled()
    {
        using var doc = LogDocument.Open(_temp.WriteFile("match\nmatch\n"));
        // Indexing never started: the query waits for lines until it is cancelled.
        using var result = doc.StartQuery(Query.Find("match"));

        result.Cancel();
        await result.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(QueryState.Cancelled, result.State);
    }

    [Fact]
    public async Task Disposing_the_document_cancels_its_queries()
    {
        var doc = LogDocument.Open(_temp.WriteFile("match\n"));
        var result = doc.StartQuery(Query.Find("match"));

        doc.Dispose();
        await result.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(QueryState.Cancelled, result.State);
    }

    [Fact]
    public async Task A_query_started_during_indexing_returns_the_same_lines_as_one_started_afterwards()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < 400_000; i++)
        {
            sb.Append(i % 101 == 0 ? "ERROR something failed " : "INFO all good ").Append(i).Append('\n');
        }

        var path = _temp.WriteFile(sb.ToString());

        using var during = LogDocument.Open(path);
        using var early = during.StartQuery(Query.Find("error")); // before indexing even starts
        var indexing = during.StartIndexing();
        using var mid = during.StartQuery(Query.Find("error"));
        await indexing;
        await Task.WhenAll(early.Completion, mid.Completion);

        using var after = LogDocument.Open(path);
        await after.StartIndexing();
        var expected = await Run(after, Query.Find("error"));

        Assert.Equal(400_000 / 101 + 1, expected.Length);
        Assert.Equal(QueryState.Completed, early.State);
        Assert.Equal(QueryState.Completed, mid.State);
        Assert.Equal(expected, Lines(early));
        Assert.Equal(expected, Lines(mid));
    }

    [Fact]
    public async Task A_last_line_without_newline_is_searched()
    {
        using var doc = await OpenIndexed("one\ntwo match");

        Assert.Equal(new long[] { 1L }, await Run(doc, Query.Find("match")));
    }

    [Fact]
    public async Task FindRow_gives_the_first_match_at_or_after_a_line()
    {
        using var doc = await OpenIndexed("x\n-\nx\n-\n-\nx\n");
        using var result = doc.StartQuery(Query.Find("x"));
        await result.Completion; // matches at 0, 2, 5

        Assert.Equal(0, result.FindRow(0));
        Assert.Equal(1, result.FindRow(1));
        Assert.Equal(1, result.FindRow(2));
        Assert.Equal(2, result.FindRow(3));
        Assert.Equal(3, result.FindRow(6));
    }

    [Fact]
    public void FindMatches_gives_every_occurrence_for_highlighting()
    {
        var query = Query.Find("ab");

        Assert.Equal([new TextMatch(0, 2), new TextMatch(4, 2)], query.FindMatches("ABcdab"));
        Assert.Empty(query.FindMatches("nothing"));
    }

    [Fact]
    public async Task Rules_combine_includes_and_excludes_and_skip_disabled_ones()
    {
        using var doc = await OpenIndexed("error db\nerror health\nwarn db\ninfo health\ninfo\n");

        var includeAndExclude = new Query([
            new QueryRule("error"), new QueryRule("warn"), new QueryRule("health", RuleKind.Exclude)]);
        var excludeOnly = new Query([new QueryRule("health", RuleKind.Exclude)]);
        var disabledInclude = new Query([
            new QueryRule("error", IsEnabled: false), new QueryRule("db", RuleKind.Exclude)]);

        Assert.Equal(new long[] { 0L, 2L }, await Run(doc, includeAndExclude));
        Assert.Equal(new long[] { 0L, 2L, 4L }, await Run(doc, excludeOnly));
        Assert.Equal(new long[] { 1L, 3L, 4L }, await Run(doc, disabledInclude));
    }
}
