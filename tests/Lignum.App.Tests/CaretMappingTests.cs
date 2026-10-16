using Lignum.App.Viewer;

namespace Lignum.App.Tests;

/// <summary>Mapping a file caret onto the screen and back: rows of a line source, display columns of a line.</summary>
public class CaretMappingTests
{
    [Theory]
    [InlineData(3, 1)]  // a line in the list
    [InlineData(0, 0)]  // before the first listed line: the first row
    [InlineData(5, 3)]  // between listed lines: the next row at or after it
    [InlineData(9, 3)]
    [InlineData(12, 4)] // past the last listed line: the last row
    public void A_line_maps_to_the_row_that_shows_it_or_the_nearest_after(long line, long expectedRow)
    {
        var source = new LineSource(new FixedLineList([1, 3, 4, 9, 10]), new NoReader());

        Assert.Equal(expectedRow, source.RowOfLine(line));
    }

    [Fact]
    public void An_empty_source_maps_every_line_to_row_0()
    {
        var source = new LineSource(new FixedLineList([]), new NoReader());

        Assert.Equal(0, source.RowOfLine(7));
    }

    [Theory]
    [InlineData("abc", 0, 0)]
    [InlineData("abc", 2, 2)]
    [InlineData("abc", 3, 3)]
    [InlineData("a\tb", 1, 1)]  // before the tab
    [InlineData("a\tb", 2, 4)]  // after the tab: the next stop
    [InlineData("\t\tx", 2, 8)]
    public void A_character_index_maps_to_a_display_column(string text, int index, int expectedColumn)
    {
        Assert.Equal(expectedColumn, TabExpansion.IndexToColumn(text, 4, index));
    }

    [Theory]
    [InlineData("abc", 2, 2)]
    [InlineData("abc", 10, 3)] // past the end: the end of the line
    [InlineData("a\tb", 1, 1)]
    [InlineData("a\tb", 2, 1)] // inside the tab's cells, nearer its start
    [InlineData("a\tb", 3, 2)] // nearer its end
    [InlineData("a\tb", 4, 2)]
    [InlineData("a\tb", 5, 3)]
    public void A_display_column_maps_to_the_nearest_character_gap(string text, int column, int expectedIndex)
    {
        Assert.Equal(expectedIndex, TabExpansion.ColumnToIndex(text, 4, column));
    }

    private sealed class FixedLineList(long[] lines) : ILineList
    {
        public long Count => lines.Length;
        public long this[long row] => lines[row];
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class NoReader : ILineReader
    {
        public string[] ReadLines(long firstLine, int count) => [];
    }
}
