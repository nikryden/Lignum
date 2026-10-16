using Lignum.Core.Searching;

namespace Lignum.Core;

/// <summary>Whether a rule selects lines (include) or hides them (exclude).</summary>
public enum RuleKind
{
    Include,
    Exclude,
}

/// <summary>
/// One condition of a <see cref="Query"/>: plain text or a regular expression, with a case setting and an
/// on/off switch. A rule with empty text counts as disabled.
/// </summary>
public sealed record QueryRule(
    string Text,
    RuleKind Kind = RuleKind.Include,
    bool IsRegex = false,
    bool MatchCase = false,
    bool IsEnabled = true);

/// <summary>A span of matched text inside one line: <see cref="Start"/> and <see cref="Length"/> in UTF-16 chars.</summary>
public readonly record struct TextMatch(int Start, int Length);

/// <summary>
/// What to look for: a list of include and exclude rules (a filter), or a single find pattern, which is
/// just one include rule. A line matches when (there are no enabled include rules, or it matches at least
/// one of them) and it matches no enabled exclude rule. Run it with <see cref="LogDocument.StartQuery"/>.
/// </summary>
/// <remarks>
/// Building a query compiles its regular expressions, so an invalid pattern throws
/// <see cref="ArgumentException"/> here, before any search starts. Regexes always run with
/// <see cref="RegexTimeout"/>; a line that takes longer fails the query with a
/// <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/>.
/// </remarks>
public sealed class Query
{
    /// <summary>Default limit for one regex match on one line.</summary>
    public static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromSeconds(2);

    public Query(IEnumerable<QueryRule> rules, TimeSpan? regexTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Rules = [.. rules];
        RegexTimeout = regexTimeout ?? DefaultRegexTimeout;
        Matcher = new LineMatcher(Rules, RegexTimeout);
    }

    /// <summary>A find-all query: lines containing <paramref name="text"/>.</summary>
    public static Query Find(string text, bool matchCase = false, bool isRegex = false) =>
        new([new QueryRule(text, RuleKind.Include, isRegex, matchCase)]);

    public IReadOnlyList<QueryRule> Rules { get; }

    public TimeSpan RegexTimeout { get; }

    internal LineMatcher Matcher { get; }

    /// <summary>Whether the line text passes this query.</summary>
    public bool IsMatch(ReadOnlySpan<char> line) => Matcher.IsMatch(line);

    /// <summary>
    /// The places in <paramref name="line"/> matched by the enabled include rules, ordered by start, for
    /// highlighting. Never throws: a regex that times out just contributes fewer matches.
    /// </summary>
    public IReadOnlyList<TextMatch> FindMatches(string line) => Matcher.FindMatches(line);
}
