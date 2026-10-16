using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lignum.App.ViewModels;

namespace Lignum.App;

// Find all (ticket 10): the find box, the results pane layout and navigation from results to the main view.
public partial class MainWindow
{
    private GridLength _resultsHeight = new(1, GridUnitType.Star);

    private SearchViewModel Search => _viewModel.Search;

    private void InitializeSearch()
    {
        Search.NavigateRequested += OnNavigateRequested;
        Search.PropertyChanged += OnSearchPropertyChanged;
        UpdateResultsPaneLayout();
    }

    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        Search.IsFindBarOpen = true;
        FindBar.FocusFindBox();
    }

    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SearchViewModel.IsResultsPaneOpen):
                UpdateResultsPaneLayout();
                break;
            case nameof(SearchViewModel.IsFindBarOpen) when !Search.IsFindBarOpen:
                LogView.Focus(); // Esc in the find box goes back to the log
                break;
            case nameof(SearchViewModel.CurrentLine) when Search.CurrentLine is null:
                LogView.MarkedLine = -1;
                ResultsView.MarkedLine = -1;
                break;
        }
    }

    /// <summary>The pane's rows collapse to nothing when it is closed and get their dragged height back when reopened.</summary>
    private void UpdateResultsPaneLayout()
    {
        if (Search.IsResultsPaneOpen)
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
        if (Search.CurrentResultRow is { } row) ResultsView.BringRowIntoView(row);
    }

    private void OnResultsMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ResultsView.RowAt(e.GetPosition(ResultsView)) is { } row) Search.GoToResult(row);
    }

    /// <summary>F3 continues from the line clicked in the main view.</summary>
    private void OnMainViewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (LogView.RowAt(e.GetPosition(LogView)) is { } row && LogView.Source is { } source)
        {
            Search.CurrentLine = source.Lines[row];
        }
    }
}
