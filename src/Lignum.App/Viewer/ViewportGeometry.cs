using System.Windows;

namespace Lignum.App.Viewer;

/// <summary>A caret position in a <see cref="LogView"/>: a row of its line source and a column (gap before a character).</summary>
public readonly record struct TextPosition(long Row, int Column);

/// <summary>
/// The layout of a <see cref="LogView"/> at one moment: where text starts, the font cell size and the scroll
/// offsets. Maps view pixels to caret positions and back (monospaced cells, no tab expansion yet).
/// </summary>
public readonly record struct ViewportGeometry(
    long FirstRow, int FirstColumn, double TextLeft, double LineHeight, double CharWidth)
{
    /// <summary>
    /// The caret position nearest to a point: the row under it and the gap between characters closest to it.
    /// Points left of the text (the margin) give the first visible column; points above or below the view
    /// give rows outside it, which callers clamp to the row count.
    /// </summary>
    public TextPosition PointToPosition(Point point)
    {
        var row = FirstRow + (long)Math.Floor(point.Y / LineHeight);
        var column = FirstColumn + (int)Math.Round((point.X - TextLeft) / CharWidth, MidpointRounding.AwayFromZero);
        return new TextPosition(row, Math.Max(FirstColumn, column));
    }

    /// <summary>Top-left corner of a position's cell in view pixels (may lie outside the view when scrolled away).</summary>
    public Point PositionToPoint(TextPosition position) => new(
        TextLeft + (position.Column - FirstColumn) * CharWidth,
        (position.Row - FirstRow) * LineHeight);
}
