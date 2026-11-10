using System.Windows;
using Lignum.App.Views;

namespace Lignum.App.Tests;

/// <summary>Where a dragged tab lands in a strip (ticket 29).</summary>
public sealed class TabDropMathTests
{
    // Three 100-wide tabs in one row, then two in a second row.
    private static readonly Rect[] OneRow = [new(0, 0, 100, 20), new(100, 0, 100, 20), new(200, 0, 100, 20)];
    private static readonly Rect[] TwoRows = [.. OneRow, new(0, 20, 100, 20), new(100, 20, 100, 20)];

    [Theory]
    [InlineData(10, 0)]   // left half of the first tab
    [InlineData(60, 1)]   // right half of the first tab
    [InlineData(149, 1)]
    [InlineData(151, 2)]
    [InlineData(260, 3)]  // right half of the last tab
    [InlineData(500, 3)]  // past the end
    public void The_insert_index_is_before_the_tab_whose_middle_is_right_of_the_mouse(double x, int expected) =>
        Assert.Equal(expected, TabDropMath.InsertIndex(OneRow, new Point(x, 10)));

    [Fact]
    public void Wrapped_tabs_use_the_mouse_row()
    {
        Assert.Equal(3, TabDropMath.InsertIndex(TwoRows, new Point(10, 30)));
        Assert.Equal(5, TabDropMath.InsertIndex(TwoRows, new Point(500, 30)));
        Assert.Equal(3, TabDropMath.InsertIndex(TwoRows, new Point(500, 10))); // end of the first row
        Assert.Equal(5, TabDropMath.InsertIndex(TwoRows, new Point(500, 60))); // below: the last row
    }

    [Fact]
    public void An_empty_strip_inserts_at_zero() => Assert.Equal(0, TabDropMath.InsertIndex([], new Point(50, 5)));

    [Theory]
    [InlineData(0, 0, 0)] // dropped where it was
    [InlineData(0, 1, 0)] // just after itself: still in place
    [InlineData(0, 3, 2)] // to the end of three
    [InlineData(2, 0, 0)]
    [InlineData(2, 1, 1)]
    public void Reordering_inside_the_strip_accounts_for_the_tab_itself(int from, int insert, int expected) =>
        Assert.Equal(expected, TabDropMath.ReorderIndex(from, insert));

    [Fact]
    public void The_marker_sits_at_the_left_edge_or_after_the_last_tab()
    {
        Assert.Equal(99, TabDropMath.MarkerBounds(OneRow, 1).Left);
        Assert.Equal(299, TabDropMath.MarkerBounds(OneRow, 3).Left);
        Assert.Equal(20, TabDropMath.MarkerBounds(OneRow, 3).Height);
    }
}
