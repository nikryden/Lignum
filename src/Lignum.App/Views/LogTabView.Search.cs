using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Lignum.App.ViewModels;
using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.Views;

// Find all (ticket 10): the find box, the results pane layout and navigation from results to the main view.
public partial class LogTabView
{
    private GridLength _resultsHeight = new(1, GridUnitType.Star);
    private SearchViewModel? _search;
    private LogTabViewModel? _tab;

    private void AttachSearch(LogTabViewModel? oldTab, LogTabViewModel? newTab)
    {
        if (oldTab is not null) oldTab.Navigation.PropertyChanged -= OnNavigationPropertyChanged;
        _tab = newTab;
        if (newTab is not null) newTab.Navigation.PropertyChanged += OnNavigationPropertyChanged;

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
        if (_tab is { } tab)
        {
            tab.Navigation.Caret = new FilePosition(line, 0); // the caret (ticket 05) goes to the result's line
            LogView.RevealCaret();
        }
        else
        {
            LogView.BringRowIntoView(line); // the main view shows every line, so row == line
        }

        if (_search?.CurrentResultRow is { } row) ResultsView.BringRowIntoView(row);
    }

    private void OnResultsMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ResultsView.RowAt(e.GetPosition(ResultsView)) is { } row) _search?.GoToResult(row);
    }

    /// <summary>F3 / Shift+F3 continue from wherever the caret is (a click, arrow keys, go to line).</summary>
    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationViewModel.Caret) && _tab is { } tab && _search is { } search)
        {
            search.CurrentLine = tab.Navigation.Caret.Line;
        }
    }
}
