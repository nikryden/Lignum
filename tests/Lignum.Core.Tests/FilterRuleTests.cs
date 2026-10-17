using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

/// <summary>
/// Filter rule combination (spec): a line is shown when (there are no enabled include rules, or it matches
/// at least one) and it matches no enabled exclude rule.
/// </summary>
public class FilterRuleTests : IDisposable
{
    private const string Log =
        "ERROR db timeout\n" +   // 0
        "error health check\n" + // 1
        "WARN db slow\n" +       // 2
        "INFO health check\n" +  // 3
        "INFO user 42 login\n";  // 4

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task<long[]> Filter(params QueryRule[] rules)
    {
        using var doc = LogDocument.Open(_temp.WriteFile(Log));
        await doc.StartIndexing();
        using var result = doc.StartQuery(new Query(rules));
        await result.Completion;
        Assert.Equal(QueryState.Completed, result.State);
        var lines = new long[result.Count];
        for (var i = 0; i < lines.Length; i++) lines[i] = result[i];
        return lines;
    }

    private async Task<long[]> Run(Query query)
    {
        using var doc = LogDocument.Open(_temp.WriteFile(Log));
        using var result = doc.StartQuery(query); // started before indexing: runs as lines arrive
        await doc.StartIndexing();
        await result.Completion;
        Assert.Equal(QueryState.Completed, result.State);
        var lines = new long[result.Count];
        for (var i = 0; i < lines.Length; i++) lines[i] = result[i];
        return lines;
    }

    [Fact]
    public async Task A_search_inside_a_filter_finds_only_lines_that_pass_both()
    {
        var filter = new Query([Include("db"), Include("health"), Exclude("INFO")]); // lines 0, 1, 2
        var search = Query.Find("o").Inside(filter);

        Assert.Equal(new long[] { 0L, 1L }, await Run(Query.Find("error").Inside(filter)));
        Assert.Equal(new long[] { 0L, 1L, 2L }, await Run(search));
        Assert.Same(filter, search.Filter);
        // Highlighting shows the search, not the filter rules.
        Assert.Equal([new TextMatch(6, 5)], Query.Find("error", matchCase: true).Inside(filter).FindMatches("ERROR error db"));
    }

    private static QueryRule Include(string text) => new(text);

    private static QueryRule Exclude(string text) => new(text, RuleKind.Exclude);

    [Fact]
    public async Task No_rules_show_every_line() =>
        Assert.Equal(new long[] { 0L, 1L, 2L, 3L, 4L }, await Filter());

    [Fact]
    public async Task Include_rules_show_lines_matching_any_of_them() =>
        Assert.Equal(new long[] { 0L, 1L, 2L }, await Filter(Include("error"), Include("warn")));

    [Fact]
    public async Task Exclude_rules_alone_hide_their_lines_from_all_lines() =>
        Assert.Equal(new long[] { 0L, 2L, 4L }, await Filter(Exclude("health")));

    [Fact]
    public async Task An_exclude_rule_wins_over_an_include_rule() =>
        Assert.Equal(new long[] { 0L, 2L }, await Filter(Include("error"), Include("warn"), Exclude("health")));

    [Fact]
    public async Task Disabled_rules_are_ignored()
    {
        Assert.Equal(new long[] { 0L, 1L, 2L, 3L, 4L }, await Filter(
            Include("error") with { IsEnabled = false }, Exclude("health") with { IsEnabled = false }));
        // With its only include rule off, an exclude-only filter is left: every line but the excluded ones.
        Assert.Equal(new long[] { 1L, 3L, 4L }, await Filter(Include("error") with { IsEnabled = false }, Exclude("db")));
    }

    [Fact]
    public async Task A_rule_with_empty_text_is_ignored() =>
        Assert.Equal(new long[] { 0L, 2L }, await Filter(Include(""), Include("db"), Exclude("")));

    [Fact]
    public async Task Each_rule_has_its_own_regex_and_case_settings() =>
        Assert.Equal(new long[] { 0L, 4L }, await Filter(
            new QueryRule("ERROR", MatchCase: true),
            new QueryRule(@"user \d+", IsRegex: true),
            new QueryRule("TIMEOUT", RuleKind.Exclude, MatchCase: true))); // "timeout" stays
}
