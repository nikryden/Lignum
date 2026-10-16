using System.Text.RegularExpressions;

namespace Lignum.Core.Searching;

/// <summary>The compiled form of a <see cref="Query"/>: decides whether one line passes.</summary>
internal sealed class LineMatcher
{
    private readonly RuleMatcher[] _includes;
    private readonly RuleMatcher[] _excludes;

    public LineMatcher(IEnumerable<QueryRule> rules, TimeSpan regexTimeout)
    {
        var active = rules.Where(r => r.IsEnabled && !string.IsNullOrEmpty(r.Text)).ToList();
        _includes = [.. active.Where(r => r.Kind == RuleKind.Include).Select(r => RuleMatcher.Create(r, regexTimeout))];
        _excludes = [.. active.Where(r => r.Kind == RuleKind.Exclude).Select(r => RuleMatcher.Create(r, regexTimeout))];
    }

    public bool IsMatch(ReadOnlySpan<char> line)
    {
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

    private abstract class RuleMatcher
    {
        public static RuleMatcher Create(QueryRule rule, TimeSpan timeout) =>
            rule.IsRegex ? new RegexMatcher(rule, timeout) : new TextMatcher(rule);

        public abstract bool IsMatch(ReadOnlySpan<char> line);

        public abstract void AddMatches(string line, List<TextMatch> matches);
    }

    /// <summary>Plain text: vectorized span <c>IndexOf</c>.</summary>
    private sealed class TextMatcher(QueryRule rule) : RuleMatcher
    {
        private readonly string _text = rule.Text;

        private readonly StringComparison _comparison =
            rule.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        public override bool IsMatch(ReadOnlySpan<char> line) => line.IndexOf(_text, _comparison) >= 0;

        public override void AddMatches(string line, List<TextMatch> matches)
        {
            var pos = 0;
            while (pos < line.Length)
            {
                var hit = line.AsSpan(pos).IndexOf(_text, _comparison);
                if (hit < 0) return;
                matches.Add(new TextMatch(pos + hit, _text.Length));
                pos += hit + _text.Length;
            }
        }
    }

    /// <summary>A compiled regex that always runs with a match timeout.</summary>
    private sealed class RegexMatcher(QueryRule rule, TimeSpan timeout) : RuleMatcher
    {
        private readonly Regex _regex = new(rule.Text,
            RegexOptions.Compiled | RegexOptions.CultureInvariant | (rule.MatchCase ? 0 : RegexOptions.IgnoreCase),
            timeout);

        public override bool IsMatch(ReadOnlySpan<char> line) => _regex.IsMatch(line);

        public override void AddMatches(string line, List<TextMatch> matches)
        {
            try
            {
                foreach (var m in _regex.EnumerateMatches(line))
                {
                    if (m.Length > 0) matches.Add(new TextMatch(m.Index, m.Length));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Highlighting is best effort; the search itself reports the timeout.
            }
        }
    }
}
