using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

// Ticket 08: the main view can hide the lines that are not in the results (the filter and / or the search).
public sealed partial class LogTabViewModel
{
    /// <summary>Show only the results' lines in the main view too (off by default); the results pane is unchanged.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewLines))]
    private bool _hideNonMatchingLines;

    /// <summary>What the main view shows: every line, or the results' lines while hiding and there are results.</summary>
    public LineSource? ViewLines => HideNonMatchingLines && Search.Results is { } results ? results : Lines;

    /// <summary>The rows the main view shows when they are not simply every line (copy uses them), else null.</summary>
    private LineSource? HidingView => HideNonMatchingLines ? Search.Results : null;

    private SearchViewModel CreateSearch()
    {
        var search = new SearchViewModel(_uiContext) { History = History };
        search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchViewModel.Results)) OnPropertyChanged(nameof(ViewLines));
        };
        return search;
    }
}
