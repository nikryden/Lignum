using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

// Find all (ticket 10): the find box, the results pane layout and navigation from results to the main view.
public partial class LogTabView
{
    private GridLength _resultsHeight = new(1, GridUnitType.Star);
    private SearchViewModel? _search;

    private void AttachSearch(LogTabViewModel? oldTab, LogTabViewModel? newTab)
    {
        if (_search is { } old)
        {
            old.NavigateRequested -= OnNavigateRequested;
            old.PropertyChanged -= OnSearchPropertyChanged;
            old.FindBoxFocusRequested -= OnFindBoxFocusRequested;
        }

        _search = newTab?.Search;
        if (_search is { } search)
        {
            search.NavigateRequested += OnNavigateRequested;
            search.PropertyChanged += OnSearchPropertyChanged;
            search.FindBoxFocusRequested += OnFindBoxFocusRequested;
        }

        UpdateResultsPaneLayout();
    }

    private void OnFindBoxFocusRequested(object? sender, EventArgs e) => FindBar.FocusFindBox();

    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SearchViewModel.IsResultsPaneOpen):
                UpdateResultsPaneLayout();
                break;
            case nameof(SearchViewModel.IsFindBarOpen) when _search is { IsFindBarOpen: false }:
                LogView.Focus(); // Esc in the find box goes back to the log
                break;
            case nameof(SearchViewModel.CurrentLine) when _search is { CurrentLine: null }:
                LogView.MarkedLine = -1;
                ResultsView.MarkedLine = -1;
                break;
        }
    }

    /// <summary>The pane's rows collapse to nothing when it is closed and get their dragged height back when reopened.</summary>
    private void UpdateResultsPaneLayout()
    {
        if (_search is { IsResultsPaneOpen: true })
        {
            SplitterRow.Height = GridLength.Auto;
            ResultsRow.Height = _resultsHeight;
        }
        else
        {
            if (ResultsRow.Height.Value > 0) _resultsHeight = ResultsRow.Height;
            SplitterRow.Height = new GridLength(0);
            ResultsRow.Height = new GridLength(0);
        }
    }

    /// <summary>A result was chosen (click, F3): show the line in the main view and keep its result in view.</summary>
    private void OnNavigateRequested(object? sender, long line)
    {
        LogView.MarkedLine = line;
        ResultsView.MarkedLine = line;
        LogView.BringRowIntoView(line); // the main view shows every line, so row == line
        if (_search?.CurrentResultRow is { } row) ResultsView.BringRowIntoView(row);
    }

    private void OnResultsMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ResultsView.RowAt(e.GetPosition(ResultsView)) is { } row) _search?.GoToResult(row);
    }

    /// <summary>F3 continues from the line clicked in the main view.</summary>
    private void OnMainViewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_search is { } search && LogView.RowAt(e.GetPosition(LogView)) is { } row && LogView.Source is { } source)
        {
            search.CurrentLine = source.Lines[row];
        }
    }
}
