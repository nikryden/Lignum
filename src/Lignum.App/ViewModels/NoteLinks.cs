using System.Text.RegularExpressions;

namespace Lignum.App.ViewModels;

/// <summary>A web link in a bookmark note (ticket 23): where it is in the note, and its address.</summary>
public sealed record NoteLink(int Start, int Length, string Target);

/// <summary>Finds <c>http(s)://</c> addresses in bookmark notes. File paths are plain text.</summary>
public static partial class NoteLinks
{
    [GeneratedRegex("""https?://[^\s"<>]+""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
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
                // A sentence's punctuation after a link is not part of it ("see https://example.com.").
                var target = match.Value.TrimEnd(TrailingPunctuation);
                links.Add(new NoteLink(match.Index, target.Length, target));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return links;
        }

        return links;
    }
}
