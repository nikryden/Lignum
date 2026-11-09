using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lignum.App.ViewModels;

// Ticket 27: results are not extended when the file grows; they say they are outdated and can be run again.
public sealed partial class SearchViewModel
{
    /// <summary>The file has grown since the shown results were found, so lines after the old end are missing.</summary>
    [ObservableProperty]
    private bool _isOutdated;

    /// <summary>The tab's file grew: mark the results shown (if any) as outdated.</summary>
    internal void MarkOutdated()
    {
        if (Results is not null) IsOutdated = true;
    }

    /// <summary>Runs the current search and filter again over the whole file as it is now.</summary>
    [RelayCommand]
    private Task RunAgain() => Rerun();
}
