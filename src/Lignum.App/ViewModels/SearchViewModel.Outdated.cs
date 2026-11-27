using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.Core;

namespace Lignum.App.ViewModels;

// Ticket 27: when the file grows the results say they are outdated and can be run again. With the results'
// own auto update on (offered while the tab auto updates), the new lines are searched and their matches added.
public sealed partial class SearchViewModel
{
    /// <summary>The file has grown since the shown results were found, so lines after the old end are missing.</summary>
    [ObservableProperty]
    private bool _isOutdated;

    /// <summary>Search the lines the file grows by and add their matches, instead of marking the results outdated.</summary>
    [ObservableProperty]
    private bool _autoUpdateResults;

    /// <summary>
    /// The tab's file grew. While the tab auto updates (<paramref name="tabAutoUpdates"/>) and so do the results,
    /// the results are extended; otherwise the results shown (if any) are marked outdated.
    /// </summary>
    internal void OnFileGrew(bool tabAutoUpdates)
    {
        if (tabAutoUpdates && AutoUpdateResults) ExtendResults();
        else MarkOutdated();
    }

    private void MarkOutdated()
    {
        if (Results is not null) IsOutdated = true;
    }

    partial void OnAutoUpdateResultsChanged(bool value)
    {
        // Turning it on catches up with growth the results missed.
        if (value && IsOutdated) ExtendResults();
    }

    /// <summary>Searches the lines indexed since the shown query stopped; the same rows stay, so the pane keeps its place.</summary>
    private void ExtendResults()
    {
        if (_document is not { } document || _result is not { } result) return;
        if (document.ExtendQuery(result))
        {
            IsOutdated = false;
            Refresh(result); // its Changed events refresh the pane as matches arrive and when it stops
            return;
        }

        if (result.State == QueryState.Running)
        {
            // Still finishing the lines it had: extend once it stops.
            result.Completion.ContinueWith(_ => RunOnUi(() =>
            {
                if (ReferenceEquals(result, _result)) ExtendResults();
            }), TaskScheduler.Default);
        }
        else
        {
            MarkOutdated(); // cancelled or failed: Run again searches the whole file
        }
    }

    /// <summary>Runs the current search and filter again over the whole file as it is now.</summary>
    [RelayCommand]
    private Task RunAgain() => Rerun();
}
