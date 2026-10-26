namespace Lignum.Core.Searching;

/// <summary>
/// A case-insensitive "contains" pattern where <c>*</c> matches any run of characters (<c>disk*full</c>). Used to
/// search bookmark notes. No regex: the parts are found left to right with <c>IndexOf</c>, so it cannot backtrack.
/// </summary>
public sealed class WildcardPattern
{
    private readonly string[] _parts;

    public WildcardPattern(string pattern) =>
        _parts = pattern.Split('*', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether the pattern matches nothing more than "anything" (empty or only <c>*</c>).</summary>
    public bool MatchesAll => _parts.Length == 0;

    /// <summary>Whether <paramref name="text"/> contains the parts, in order, anywhere.</summary>
    public bool IsMatch(ReadOnlySpan<char> text)
    {
        foreach (var part in _parts)
        {
            var at = text.IndexOf(part, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;
            text = text[(at + part.Length)..];
        }

        return true;
    }
}
