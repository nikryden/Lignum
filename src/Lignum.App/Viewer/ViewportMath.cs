namespace Lignum.App.Viewer;

/// <summary>
/// Pure scroll and hit-test math for <see cref="LogView"/>. Rows and columns are the scroll units:
/// the vertical scroll offset is the first visible row, the horizontal one the first visible column.
/// </summary>
public static class ViewportMath
{
    /// <summary>
    /// Clamps a first row (or column) so the view never scrolls before 0 or past the point where the last one sits
    /// at the end of a full page.
    /// </summary>
    public static long ClampFirst(long first, long extent, int viewport) =>
        Math.Max(0, Math.Min(first, extent - Math.Max(1, viewport)));

    /// <summary>
    /// Converts a scroll offset from WPF (a double, fractional while the thumb is dragged) to the first
    /// visible row or column, rounded to the nearest and clamped like <see cref="ClampFirst"/>.
    /// Doubles hold whole numbers exactly up to 2^53, far beyond any line count.
    /// </summary>
    public static long OffsetToFirst(double offset, long extent, int viewport)
    {
        var max = ClampFirst(long.MaxValue, extent, viewport);
        if (double.IsNaN(offset) || offset <= 0) return 0;
        if (offset >= max) return max;
        return (long)Math.Round(offset, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Whole rows (or columns) of size <paramref name="unit"/> that fit in <paramref name="length"/>; at least 1
    /// when there is any space, so paging and clamping always make progress.
    /// </summary>
    public static int FullyVisible(double length, double unit)
    {
        if (unit <= 0 || length <= 0) return 0;
        return Math.Max(1, (int)Math.Floor(length / unit));
    }
}
