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

    /// <summary>The display column of the gap before <paramref name="index"/> in <paramref name="text"/> (clamped to the end).</summary>
    public static int IndexToColumn(string text, int tabWidth, int index)
    {
        tabWidth = Math.Max(1, tabWidth);
        var end = Math.Clamp(index, 0, text.Length);
        var column = 0;
        for (var i = 0; i < end; i++) column = NextColumn(text[i], column, tabWidth);
        return column;
    }

    /// <summary>
    /// The character gap nearest to display <paramref name="column"/>: a click inside a tab goes to whichever
    /// side is closer, a column past the end of the line goes to the end.
    /// </summary>
    public static int ColumnToIndex(string text, int tabWidth, int column)
    {
        tabWidth = Math.Max(1, tabWidth);
        var current = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (current >= column) return i;
            var next = NextColumn(text[i], current, tabWidth);
            if (next > column) return column - current <= next - column ? i : i + 1;
            current = next;
        }

        return text.Length;
    }

    private static int NextColumn(char c, int column, int tabWidth) =>
        c == '\t' ? column + tabWidth - column % tabWidth : column + 1;
}
