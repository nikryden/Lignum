namespace Lignum.Core.Searching;

/// <summary>The compiled form of a <see cref="Query"/>: decides whether one line passes.</summary>
internal sealed class LineMatcher
{
    private readonly TextPattern[] _includes;
    private readonly TextPattern[] _excludes;
    private readonly LineMatcher? _within;

    /// <param name="within">Lines must also pass this matcher (a search inside a filter); null: no extra condition.</param>
    public LineMatcher(IEnumerable<QueryRule> rules, TimeSpan regexTimeout, LineMatcher? within = null)
    {
        _within = within;
        var active = rules.Where(r => r.IsEnabled && !string.IsNullOrEmpty(r.Text)).ToList();
        _includes = [.. active.Where(r => r.Kind == RuleKind.Include).Select(r => Create(r, regexTimeout))];
        _excludes = [.. active.Where(r => r.Kind == RuleKind.Exclude).Select(r => Create(r, regexTimeout))];
    }

    private static TextPattern Create(QueryRule rule, TimeSpan regexTimeout) =>
        TextPattern.Create(rule.Text, rule.IsRegex, rule.MatchCase, regexTimeout, rule.NonBacktracking);

    public bool IsMatch(ReadOnlySpan<char> line)
    {
        if (_within is not null && !_within.IsMatch(line)) return false;

        if (_includes.Length > 0)
        {
            var included = false;
            foreach (var m in _includes)
            {
                if (m.IsMatch(line))
                {
                    included = true;
                    break;
                }
            }

            if (!included) return false;
        }

        foreach (var m in _excludes)
        {
            if (m.IsMatch(line)) return false;
        }

        return true;
    }

    public IReadOnlyList<TextMatch> FindMatches(string line)
    {
        if (_includes.Length == 0 || line.Length == 0) return [];
        var matches = new List<TextMatch>();
        foreach (var m in _includes) m.AddMatches(line, matches);
        if (_includes.Length > 1) matches.Sort((a, b) => a.Start.CompareTo(b.Start));
        return matches;
    }
}
