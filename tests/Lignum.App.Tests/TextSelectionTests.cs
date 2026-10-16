using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.Tests;

public class TextSelectionTests
{
    [Fact]
    public void A_new_selection_is_empty_at_the_start_of_the_file()
    {
        var selection = new TextSelection();

        Assert.True(selection.IsEmpty);
        Assert.Equal(new FilePosition(0, 0), selection.Caret);
    }

    [Fact]
    public void Placing_the_caret_clears_the_selection()
    {
        var selection = new TextSelection();
        selection.MoveCaret(new FilePosition(3, 5), extend: true);

        selection.MoveCaret(new FilePosition(7, 2), extend: false);

        Assert.True(selection.IsEmpty);
        Assert.Equal(new FilePosition(7, 2), selection.Anchor);
        Assert.Equal(new FilePosition(7, 2), selection.Caret);
    }

    [Fact]
    public void Extending_keeps_the_anchor_and_orders_start_and_end()
    {
        var selection = new TextSelection();
        selection.MoveCaret(new FilePosition(2_000_000, 4), extend: false);

        selection.MoveCaret(new FilePosition(10, 1), extend: true); // Shift+Click far above

        Assert.False(selection.IsEmpty);
        Assert.Equal(new FilePosition(2_000_000, 4), selection.Anchor);
        Assert.Equal(new FilePosition(10, 1), selection.Start);
        Assert.Equal(new FilePosition(2_000_000, 4), selection.End);
    }

    [Fact]
    public void Select_all_runs_from_the_start_to_the_end_of_the_last_line()
    {
        var selection = new TextSelection();

        selection.SelectAll(lineCount: 42);

        Assert.Equal(new FilePosition(0, 0), selection.Start);
        Assert.Equal(new FilePosition(41, int.MaxValue), selection.End);
    }

    [Fact]
    public void Select_all_on_an_empty_file_selects_nothing()
    {
        var selection = new TextSelection();

        selection.SelectAll(lineCount: 0);

        Assert.True(selection.IsEmpty);
    }

    [Theory]
    [InlineData(4, 0, 0, false)]
    [InlineData(5, 3, int.MaxValue, true)]       // first line: from the start column to its end
    [InlineData(6, 0, int.MaxValue, true)]       // a middle line: all of it, including its line break
    [InlineData(8, 0, 2, true)]                  // last line: up to the end column
    [InlineData(9, 0, 0, false)]
    public void Each_line_reports_the_part_that_is_selected(long line, int from, int to, bool selected)
    {
        var selection = new TextSelection();
        selection.MoveCaret(new FilePosition(8, 2), extend: false);
        selection.MoveCaret(new FilePosition(5, 3), extend: true);

        var range = selection.ColumnsOn(line);

        Assert.Equal(selected, range is not null);
        if (range is { } r)
        {
            Assert.Equal(from, r.From);
            Assert.Equal(to, r.To);
        }
    }

    [Fact]
    public void Changes_are_announced()
    {
        var selection = new TextSelection();
        var changes = 0;
        selection.Changed += (_, _) => changes++;

        selection.MoveCaret(new FilePosition(1, 1), extend: true);
        selection.MoveCaret(new FilePosition(1, 1), extend: true); // no change
        selection.SelectAll(3);

        Assert.Equal(2, changes);
    }
}
