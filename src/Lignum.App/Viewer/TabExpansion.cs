using System.Text;

namespace Lignum.App.Viewer;

/// <summary>Turns tabs into spaces up to the next tab stop, so indented content lines up.</summary>
public static class TabExpansion
{
    /// <summary>
    /// Expands tabs in <paramref name="text"/> with stops every <paramref name="tabWidth"/> columns and
    /// returns at most <paramref name="maxColumns"/> columns. Returns the input itself when there is nothing to do.
    /// </summary>
    public static string Expand(string text, int tabWidth, int maxColumns = int.MaxValue)
    {
        tabWidth = Math.Max(1, tabWidth);
        var firstTab = text.IndexOf('\t');
        if (firstTab < 0 || firstTab >= maxColumns) return text.Length > maxColumns ? text[..maxColumns] : text;

        var sb = new StringBuilder(Math.Min(maxColumns, text.Length + 16));
        sb.Append(text, 0, firstTab);
        for (var i = firstTab; i < text.Length && sb.Length < maxColumns; i++)
        {
            var c = text[i];
            if (c == '\t') sb.Append(' ', tabWidth - sb.Length % tabWidth);
            else sb.Append(c);
        }

        if (sb.Length > maxColumns) sb.Length = maxColumns;
        return sb.ToString();
    }
}
