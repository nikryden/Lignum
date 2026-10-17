using CommunityToolkit.Mvvm.ComponentModel;

namespace Lignum.App.ViewModels;

// Ticket 08: app-wide search settings that every tab follows.
public sealed partial class MainViewModel
{
    /// <summary>
    /// Highlight search matches in the full log even on lines the filter rules hide (on by default). Off:
    /// only lines that pass the filter are highlighted. Saved in settings.json.
    /// </summary>
    [ObservableProperty]
    private bool _highlightMatchesOutsideFilter = true;

    partial void OnHighlightMatchesOutsideFilterChanged(bool value)
    {
        foreach (var tab in Tabs) tab.Search.HighlightOutsideFilter = value;
        if (_settings.Value.HighlightMatchesOutsideFilter == value) return;
        _settings.Value.HighlightMatchesOutsideFilter = value;
        _settings.Save();
    }
}
