using System.Text.RegularExpressions;

namespace Lignum.App.ViewModels;

public enum NoteLinkKind
{
    /// <summary>An <c>http(s)://</c> address: opens in the default browser.</summary>
    Web,

    /// <summary>A drive or UNC path: a file opens in a tab, a folder in Explorer.</summary>
    Path,
}

/// <summary>A link in a bookmark note (ticket 23): where it is in the note, and what it points to.</summary>
public sealed record NoteLink(int Start, int Length, string Target, NoteLinkKind Kind);

/// <summary>
/// Finds links in bookmark notes: web addresses, and anything that looks like a drive (<c>C:\…</c>) or UNC
/// (<c>\\server\…</c>) path, whether or not it exists. A path with spaces needs quotes (<c>"C:\My logs\a.log"</c>).
/// </summary>
public static partial class NoteLinks
{
    // Quoted paths first (the quotes are not part of the target), then bare paths, then web addresses.
    [GeneratedRegex("""
        "(?<path>(?:[A-Za-z]:\\|\\\\[^\\\s"]+\\)[^"\r\n]*)"
        |(?<path>(?:[A-Za-z]:\\|\\\\[^\\\s"<>|]+\\)[^\s"<>|?*]*)
        |(?<web>https?://[^\s"<>]+)
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex LinkRegex();

    private static readonly char[] TrailingPunctuation = ['.', ',', ';', ':', ')', ']', '}', '!', '?', '\''];

    /// <summary>The links in <paramref name="note"/>, in order; empty when it has none (or the match times out).</summary>
    public static IReadOnlyList<NoteLink> Find(string? note)
    {
        if (string.IsNullOrEmpty(note)) return [];
        var links = new List<NoteLink>();
        try
        {
            foreach (Match match in LinkRegex().Matches(note))
            {
                var web = match.Groups["web"].Success;
                var group = web ? match.Groups["web"] : match.Groups["path"];
                var quoted = !web && group.Index > match.Index;
                // A sentence's punctuation after a bare link is not part of it ("see C:\logs\a.log.").
                var target = quoted ? group.Value : group.Value.TrimEnd(TrailingPunctuation);
                if (target.Length == 0 || (!web && target.EndsWith(':'))) continue;
                links.Add(new NoteLink(group.Index, target.Length, target, web ? NoteLinkKind.Web : NoteLinkKind.Path));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return links;
        }

        return links;
    }

    /// <summary>The link that covers character <paramref name="index"/> of <paramref name="note"/>, or null.</summary>
    public static NoteLink? At(string? note, int index) =>
        Find(note).FirstOrDefault(l => index >= l.Start && index < l.Start + l.Length);
}
