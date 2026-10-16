using Lignum.App.Viewer;

namespace Lignum.App.Tests;

public class TabExpansionTests
{
    [Theory]
    [InlineData("a\tb", 4, "a   b")]
    [InlineData("\tx", 4, "    x")]
    [InlineData("abcd\tx", 4, "abcd    x")]
    [InlineData("ab\t\tc", 4, "ab      c")]
    [InlineData("a\tb", 8, "a       b")]
    [InlineData("a\tb", 2, "a b")]
    [InlineData("no tabs", 4, "no tabs")]
    [InlineData("", 4, "")]
    public void Tabs_expand_to_the_next_tab_stop(string text, int tabWidth, string expected)
    {
        Assert.Equal(expected, TabExpansion.Expand(text, tabWidth));
    }

    [Fact]
    public void Expansion_can_stop_after_a_number_of_columns()
    {
        // The viewer only needs the columns that fit on screen, even for a huge line.
        Assert.Equal("a   b", TabExpansion.Expand("a\tbcdef\t\tg", 4, maxColumns: 5));
    }

    [Theory]
    [InlineData("abc", 2, 2)]
    [InlineData("a\tb", 1, 1)]   // before the tab
    [InlineData("a\tb", 2, 4)]   // after the tab: the tab spans columns 1..3
    [InlineData("a\tb", 3, 5)]   // end of line
    [InlineData("\t\tx", 2, 8)]
    [InlineData("abc", 10, 10)]  // past the end: one column per missing character
    public void A_character_index_maps_to_its_display_column(string text, int index, int expected)
    {
        Assert.Equal(expected, TabExpansion.ToDisplayColumn(text, 4, index));
    }

    [Theory]
    [InlineData("abc", 2, 2)]
    [InlineData("a\tb", 1, 1)]
    [InlineData("a\tb", 2, 1)]   // inside the tab, nearer its start
    [InlineData("a\tb", 3, 2)]   // inside the tab, nearer its end
    [InlineData("a\tb", 4, 2)]
    [InlineData("a\tb", 5, 3)]
    [InlineData("abc", 10, 3)]   // past the end of the line: the end
    public void A_display_column_maps_to_the_nearest_character_gap(string text, int column, int expected)
    {
        Assert.Equal(expected, TabExpansion.ToCharIndex(text, 4, column));
    }
}
