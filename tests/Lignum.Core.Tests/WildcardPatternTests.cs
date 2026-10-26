using Lignum.Core.Searching;

namespace Lignum.Core.Tests;

/// <summary>Bookmark note search (ticket 22): case-insensitive contains, <c>*</c> = any run of characters.</summary>
public class WildcardPatternTests
{
    [Theory]
    [InlineData("disk", "Disk full on /var", true)]
    [InlineData("disk*var", "Disk full on /var", true)]
    [InlineData("var*disk", "Disk full on /var", false)]
    [InlineData("*full*", "Disk full on /var", true)]
    [InlineData("d*k*l", "disk full", true)]
    [InlineData("aa*aa", "aaa", false)]
    [InlineData("timeout", "Disk full", false)]
    [InlineData("", "anything", true)]
    [InlineData("**", "", true)]
    public void Matches_the_parts_in_order_anywhere(string pattern, string text, bool expected) =>
        Assert.Equal(expected, new WildcardPattern(pattern).IsMatch(text));

    [Fact]
    public void An_empty_or_star_only_pattern_matches_all()
    {
        Assert.True(new WildcardPattern("").MatchesAll);
        Assert.True(new WildcardPattern("*").MatchesAll);
        Assert.False(new WildcardPattern("a*").MatchesAll);
    }
}
