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

    /// <summary>
    /// The display column of the gap before character <paramref name="index"/> of <paramref name="text"/>.
    /// Indexes past the end count one column per missing character.
    /// </summary>
    public static int ToDisplayColumn(string text, int tabWidth, int index)
    {
        tabWidth = Math.Max(1, tabWidth);
        var end = Math.Min(Math.Max(0, index), text.Length);
        var column = 0L;
        for (var i = 0; i < end; i++) column = Advance(column, text[i], tabWidth);
        return (int)Math.Min(int.MaxValue, column + Math.Max(0, (long)index - end));
    }

    /// <summary>
    /// The character gap nearest to display column <paramref name="column"/>: inside a tab it snaps to the
    /// nearer side; past the end of the line it gives the end.
    /// </summary>
    public static int ToCharIndex(string text, int tabWidth, int column)
    {
        tabWidth = Math.Max(1, tabWidth);
        var current = 0L;
        for (var i = 0; i < text.Length; i++)
        {
            if (current >= column) return i;
            var next = Advance(current, text[i], tabWidth);
            if (next > column) return column - current <= next - column ? i : i + 1;
            current = next;
        }

        return text.Length;
    }

    private static long Advance(long column, char c, int tabWidth) =>
        c == '\t' ? column + tabWidth - column % tabWidth : column + 1;
}
