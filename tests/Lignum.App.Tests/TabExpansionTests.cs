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
    [InlineData("a	b", 2, 4)]
    [InlineData("a	b", 3, 5)]
    [InlineData("		x", 2, 8)]
    [InlineData("ab", 5, 2)]
    public void A_string_index_maps_to_its_display_column(string text, int index, int expected)
    {
        // Search matches are found in the raw text but drawn in tab-expanded columns.
        Assert.Equal(expected, TabExpansion.ColumnOf(text, index, tabWidth: 4));
    }
}
