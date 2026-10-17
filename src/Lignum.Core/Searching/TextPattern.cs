using System.Text.RegularExpressions;

namespace Lignum.Core.Searching;

/// <summary>
/// One plain-text or regex pattern, compiled once and matched against many lines. Shared by search, filter
/// rules and line highlighting so they all match the same way. Regexes always run with a match timeout.
/// </summary>
public abstract class TextPattern
{
    /// <summary>
    /// Compiles a pattern. Throws <see cref="ArgumentException"/> when <paramref name="text"/> is not a valid
    /// regex (including one the NonBacktracking engine does not support).
    /// </summary>
    public static TextPattern Create(string text, bool isRegex, bool matchCase, TimeSpan regexTimeout,
                                     bool nonBacktracking = false) =>
        isRegex ? new RegexPattern(text, matchCase, nonBacktracking, regexTimeout) : new PlainPattern(text, matchCase);

    /// <summary>Whether the line contains the pattern. A regex that times out throws <see cref="RegexMatchTimeoutException"/>.</summary>
    public abstract bool IsMatch(ReadOnlySpan<char> line);

    /// <summary>Adds every non-empty match in <paramref name="line"/>. Best effort: a regex timeout just stops.</summary>
    public abstract void AddMatches(string line, List<TextMatch> matches);

    /// <summary>Plain text: vectorized span <c>IndexOf</c>.</summary>
    private sealed class PlainPattern(string text, bool matchCase) : TextPattern
    {
        private readonly StringComparison _comparison =
            matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        public override bool IsMatch(ReadOnlySpan<char> line) => line.IndexOf(text, _comparison) >= 0;

        public override void AddMatches(string line, List<TextMatch> matches)
        {
            var pos = 0;
            while (pos < line.Length)
            {
                var hit = line.AsSpan(pos).IndexOf(text, _comparison);
                if (hit < 0) return;
                matches.Add(new TextMatch(pos + hit, text.Length));
                pos += hit + text.Length;
            }
        }
    }

    /// <summary>
    /// A compiled regex that always runs with a match timeout (the NonBacktracking engine honours it too).
    /// .NET accepts <c>Compiled</c> together with <c>NonBacktracking</c> (it compiles the automaton);
    /// only <c>RightToLeft</c>/<c>ECMAScript</c> are rejected with it, and we never set those.
    /// </summary>
    private sealed class RegexPattern(string text, bool matchCase, bool nonBacktracking, TimeSpan timeout) : TextPattern
    {
        private readonly Regex _regex = Build(text, matchCase, nonBacktracking, timeout);

        private static Regex Build(string text, bool matchCase, bool nonBacktracking, TimeSpan timeout)
        {
            var options = RegexOptions.Compiled | RegexOptions.CultureInvariant
                | (matchCase ? 0 : RegexOptions.IgnoreCase)
                | (nonBacktracking ? RegexOptions.NonBacktracking : 0);
            try
            {
                return new Regex(text, options, timeout);
            }
            catch (NotSupportedException ex)
            {
                // Backreferences, lookarounds, atomic groups... under NonBacktracking. Report it like any
                // other invalid pattern so callers handle a single exception type.
                throw new ArgumentException(
                    "The NonBacktracking engine does not support this pattern: " + ex.Message, nameof(text), ex);
            }
        }

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
