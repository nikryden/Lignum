using System.Windows;

namespace Lignum.App.Views;

/// <summary>Where a dragged tab lands in a tab strip (ticket 29), from the tabs' bounds and the mouse.</summary>
internal static class TabDropMath
{
    /// <summary>
    /// The index the tab would be inserted at (before the tab that is now there), given the strip's tabs
    /// in order and the mouse point in the same coordinates. Only the tabs in the mouse's row count when
    /// the tabs wrap to several rows; past the last tab of a row means after it.
    /// </summary>
    public static int InsertIndex(IReadOnlyList<Rect> tabs, Point point)
    {
        if (tabs.Count == 0) return 0;

        var row = Enumerable.Range(0, tabs.Count).Where(i => point.Y >= tabs[i].Top && point.Y <= tabs[i].Bottom).ToList();
        if (row.Count == 0)
        {
            // Above every row: the first row; below: the last row.
            var y = point.Y < tabs.Min(t => t.Top) ? tabs.Min(t => t.Top) : tabs.Max(t => t.Top);
            row = [.. Enumerable.Range(0, tabs.Count).Where(i => tabs[i].Top == y)];
        }

        foreach (var i in row)
        {
            if (point.X < tabs[i].Left + tabs[i].Width / 2) return i;
        }
        return row[^1] + 1;
    }

    /// <summary>
    /// The final index of a tab moved inside its own strip from <paramref name="from"/> to insertion point
    /// <paramref name="insert"/> (as <see cref="InsertIndex"/> gives it, counting the tab itself).
    /// </summary>
    public static int ReorderIndex(int from, int insert) => insert > from ? insert - 1 : insert;

    /// <summary>The marker for insertion point <paramref name="insert"/>: the left edge of that tab, or the right edge of the one before.</summary>
    public static Rect MarkerBounds(IReadOnlyList<Rect> tabs, int insert)
    {
        if (tabs.Count == 0) return new Rect(0, 0, 2, 20);
        var tab = insert < tabs.Count ? tabs[insert] : tabs[^1];
        var x = insert < tabs.Count ? tab.Left : tab.Right;
        return new Rect(Math.Max(0, x - 1), tab.Top, 2, tab.Height);
    }
}
