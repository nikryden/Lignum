using Lignum.App.ViewModels;
using Lignum.App.Viewer;

namespace Lignum.App.Tests;

public class NavigationViewModelTests
{
    private long _lineCount = 100;

    private NavigationViewModel Create() => new(() => _lineCount);

    [Fact]
    public void Go_to_line_moves_the_caret_to_the_start_of_that_line()
    {
        var nav = Create();
        nav.Caret = new FilePosition(5, 7);

        var ok = nav.TryGoToLine("42", out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new FilePosition(41, 0), nav.Caret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12x")]
    [InlineData("1.5")]
    [InlineData(null)]
    public void Go_to_line_rejects_text_that_is_not_a_number_and_leaves_the_caret(string? input)
    {
        var nav = Create();
        nav.Caret = new FilePosition(5, 7);

        var ok = nav.TryGoToLine(input, out var error);

        Assert.False(ok);
        Assert.Equal("Enter a line number.", error);
        Assert.Equal(new FilePosition(5, 7), nav.Caret);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("101")]
    [InlineData("99999999999999999999999")]
    public void Go_to_line_rejects_lines_outside_the_file_and_names_the_range(string input)
    {
        var nav = Create();
        nav.Caret = new FilePosition(5, 7);

        var ok = nav.TryGoToLine(input, out var error);

        Assert.False(ok);
        Assert.Equal("Enter a line between 1 and 100.", error);
        Assert.Equal(new FilePosition(5, 7), nav.Caret);
    }

    [Fact]
    public void Go_to_line_accepts_surrounding_spaces_and_the_first_and_last_line()
    {
        var nav = Create();

        Assert.True(nav.TryGoToLine(" 100 ", out _));
        Assert.Equal(new FilePosition(99, 0), nav.Caret);
        Assert.True(nav.TryGoToLine("1", out _));
        Assert.Equal(FilePosition.Start, nav.Caret);
    }

    [Fact]
    public void Go_to_line_with_no_lines_says_there_is_nothing_to_go_to()
    {
        _lineCount = 0;
        var nav = Create();

        Assert.False(nav.TryGoToLine("1", out var error));
        Assert.Equal("There are no lines to go to.", error);
    }

    [Fact]
    public void Go_to_end_puts_the_caret_on_the_last_line_known_so_far()
    {
        var nav = Create();

        nav.GoToEndCommand.Execute(null);
        Assert.Equal(new FilePosition(99, 0), nav.Caret);

        _lineCount = 250; // indexing found more lines
        nav.GoToEndCommand.Execute(null);
        Assert.Equal(new FilePosition(249, 0), nav.Caret);
    }

    [Fact]
    public void Go_to_start_puts_the_caret_on_the_first_line()
    {
        var nav = Create();
        nav.Caret = new FilePosition(50, 3);

        nav.GoToStartCommand.Execute(null);

        Assert.Equal(FilePosition.Start, nav.Caret);
    }

    [Fact]
    public void Every_jump_asks_the_view_to_reveal_the_caret_even_when_it_does_not_move()
    {
        var nav = Create();
        var reveals = 0;
        nav.RevealRequested += (_, _) => reveals++;

        nav.GoToStartCommand.Execute(null); // already at the start, but the view may be scrolled away
        nav.GoToEndCommand.Execute(null);
        nav.TryGoToLine("10", out _);
        nav.TryGoToLine("nope", out _); // a rejected jump does not

        Assert.Equal(3, reveals);
    }

    [Fact]
    public void Go_to_end_of_an_empty_file_stays_at_the_start()
    {
        _lineCount = 0;
        var nav = Create();

        nav.GoToEndCommand.Execute(null);

        Assert.Equal(FilePosition.Start, nav.Caret);
    }

    [Fact]
    public void The_status_text_shows_the_caret_as_1_based_line_and_column_and_follows_it()
    {
        var nav = Create();
        Assert.Equal("Ln 1, Col 1", nav.CaretText);
        var changed = new List<string?>();
        nav.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        nav.Caret = new FilePosition(41, 9);

        Assert.Equal("Ln 42, Col 10", nav.CaretText);
        Assert.Contains(nameof(NavigationViewModel.CaretText), changed);
    }

    [Fact]
    public void Reset_puts_the_caret_back_at_the_start_for_a_new_file()
    {
        var nav = Create();
        nav.Caret = new FilePosition(41, 9);

        nav.Reset();

        Assert.Equal(FilePosition.Start, nav.Caret);
    }
}
