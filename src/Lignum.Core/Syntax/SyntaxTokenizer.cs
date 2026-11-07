using System.Text.RegularExpressions;

namespace Lignum.Core.Syntax;

/// <summary>A colored range of one line: <see cref="Rule"/> is the index in the scheme's rule list.</summary>
public readonly record struct SyntaxToken(int Start, int Length, int Rule);

/// <summary>
/// Splits one line into <see cref="SyntaxToken"/>s from its text alone (ticket 25: the viewer reads only the visible
/// lines, so there is no state carried between lines). At each position the earliest match of any enabled rule wins;
/// among matches that start at the same place, the first rule wins. Text between tokens is plain.
/// Immutable and safe to share between threads.
/// </summary>
public sealed class SyntaxTokenizer
{
    /// <summary>Per match. A rule that times out is skipped for the rest of that line.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    public static readonly SyntaxTokenizer Empty = new([]);

    private readonly (Regex Regex, int Rule)[] _rules;

    public SyntaxTokenizer(IReadOnlyList<SyntaxRule> rules)
    {
        var compiled = new List<(Regex, int)>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!rule.IsEnabled || string.IsNullOrEmpty(rule.Pattern)) continue;
            if (TryCreate(rule.Pattern, rule.MatchCase, out var regex)) compiled.Add((regex!, i));
        }

        _rules = [.. compiled];
    }

    public bool IsEmpty => _rules.Length == 0;

    /// <summary>Why <paramref name="pattern"/> cannot be used (invalid regex), or null.</summary>
    public static string? Validate(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        try
        {
            _ = new Regex(pattern, RegexOptions.None, MatchTimeout);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The tokens of <paramref name="line"/>, in order and not overlapping.</summary>
    public IReadOnlyList<SyntaxToken> Tokenize(string line)
    {
        if (_rules.Length == 0 || line.Length == 0) return [];

        var tokens = new List<SyntaxToken>();
        // The next match of each rule at or after the position it was searched from; -1 start = search again,
        // int.MaxValue = no more matches. A cached match that starts at or after the current position is still
        // the leftmost one from there, so each rule is searched again only when the position passes its match.
        var starts = new int[_rules.Length];
        var lengths = new int[_rules.Length];
        Array.Fill(starts, -1);
        var position = 0;
        while (position < line.Length)
        {
            var best = -1;
            for (var r = 0; r < _rules.Length; r++)
            {
                if (starts[r] != int.MaxValue && starts[r] < position) (starts[r], lengths[r]) = Next(_rules[r].Regex, line, position);
                if (starts[r] != int.MaxValue && (best < 0 || starts[r] < starts[best])) best = r;
            }

            if (best < 0) break;
            tokens.Add(new SyntaxToken(starts[best], lengths[best], _rules[best].Rule));
            position = starts[best] + lengths[best];
        }

        return tokens;
    }

    /// <summary>The first non-empty match at or after <paramref name="from"/>, or (int.MaxValue, 0).</summary>
    private static (int Start, int Length) Next(Regex regex, string line, int from)
    {
        try
        {
            for (var at = from; at <= line.Length; at++)
            {
                var match = regex.Match(line, at);
                if (!match.Success) break;
                if (match.Length > 0) return (match.Index, match.Length);
                at = match.Index; // an empty match: look again one character later
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return (int.MaxValue, 0);
    }

    private static bool TryCreate(string pattern, bool matchCase, out Regex? regex)
    {
        try
        {
            var options = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            regex = new Regex(pattern, options, MatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            regex = null;
            return false;
        }
    }
}
