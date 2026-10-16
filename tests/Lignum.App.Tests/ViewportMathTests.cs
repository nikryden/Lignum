using System.Windows;
using Lignum.App.Viewer;

namespace Lignum.App.Tests;

public class ViewportMathTests
{
    [Theory]
    [InlineData(0, 100, 10, 0)]
    [InlineData(50, 100, 10, 50)]
    [InlineData(95, 100, 10, 90)] // can't scroll past the last full page
    [InlineData(-5, 100, 10, 0)]
    [InlineData(3, 5, 10, 0)] // fewer rows than fit: always top
    [InlineData(7, 0, 10, 0)] // empty
    public void Clamp_first_row_keeps_the_last_page_full(long first, long rowCount, int viewportRows, long expected)
    {
        Assert.Equal(expected, ViewportMath.ClampFirst(first, rowCount, viewportRows));
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(12.4, 12)]
    [InlineData(12.6, 13)] // thumb drags give fractions: nearest row
    [InlineData(25_000_000.0, 25_000_000)] // middle of a ~50M-line file, exactly
    [InlineData(49_999_990.0, 49_999_990)] // the last page
    [InlineData(60_000_000.0, 49_999_990)]
    [InlineData(-3.0, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 49_999_990)]
    public void Scroll_offset_maps_to_a_clamped_row(double offset, long expected)
    {
        Assert.Equal(expected, ViewportMath.OffsetToFirst(offset, extent: 50_000_000, viewport: 10));
    }

    [Theory]
    [InlineData(170, 17, 10)]
    [InlineData(179, 17, 10)] // a partly visible row does not count
    [InlineData(5, 17, 1)] // shorter than one row: still scrolls a row at a time
    [InlineData(0, 17, 0)]
    [InlineData(100, 0, 0)] // no metrics yet
    public void Fully_visible_counts_whole_units(double length, double unit, int expected)
    {
        Assert.Equal(expected, ViewportMath.FullyVisible(length, unit));
    }

    // Text starts at x = 50, rows are 20 px tall, characters 8 px wide, scrolled to row 1000, column 10.
    private static readonly ViewportGeometry Geometry = new(FirstRow: 1000, FirstColumn: 10, TextLeft: 50,
        LineHeight: 20, CharWidth: 8);

    [Theory]
    [InlineData(50, 0, 1000, 10)] // top-left of the text area
    [InlineData(53, 19.9, 1000, 10)] // left half of the first character -> before it
    [InlineData(55, 20, 1001, 11)] // right half -> after it
    [InlineData(50 + 8 * 5, 45, 1002, 15)]
    [InlineData(10, 30, 1001, 10)] // in the line-number margin: first visible column
    [InlineData(80, -1, 999, 14)] // above the view (drag-select upwards)
    public void Point_maps_to_the_nearest_caret_position(double x, double y, long row, int column)
    {
        Assert.Equal(new TextPosition(row, column), Geometry.PointToPosition(new Point(x, y)));
    }

    [Theory]
    [InlineData(1000, 10, 50, 0)]
    [InlineData(1003, 15, 90, 60)]
    [InlineData(998, 4, 2, -40)] // scrolled out of view: outside the view's bounds
    public void Position_maps_to_the_top_left_of_its_cell(long row, int column, double x, double y)
    {
        Assert.Equal(new Point(x, y), Geometry.PositionToPoint(new TextPosition(row, column)));
    }

    [Fact]
    public void Position_round_trips_through_its_point()
    {
        var position = new TextPosition(1017, 123);
        Assert.Equal(position, Geometry.PointToPosition(Geometry.PositionToPoint(position)));
    }

    [Fact]
    public void One_wheel_notch_scrolls_the_system_line_count()
    {
        var wheel = new WheelAccumulator();
        Assert.Equal(3, wheel.Add(-120, linesPerNotch: 3)); // wheel towards the user = down
        Assert.Equal(-6, wheel.Add(240, linesPerNotch: 3));
    }

    [Fact]
    public void Small_touchpad_deltas_add_up_to_whole_rows()
    {
        var wheel = new WheelAccumulator();
        Assert.Equal(0, wheel.Add(-30, linesPerNotch: 3)); // 0.75 row
        Assert.Equal(1, wheel.Add(-30, linesPerNotch: 3)); // 1.5 rows
        Assert.Equal(1, wheel.Add(-20, linesPerNotch: 3)); // 2.0 rows
    }

    [Fact]
    public void Changing_direction_drops_the_leftover()
    {
        var wheel = new WheelAccumulator();
        Assert.Equal(0, wheel.Add(-30, linesPerNotch: 3));
        Assert.Equal(0, wheel.Add(30, linesPerNotch: 3)); // 0.75 up, not 0.75 - 0.75
        Assert.Equal(-1, wheel.Add(10, linesPerNotch: 3)); // 1.0 up
    }
}
