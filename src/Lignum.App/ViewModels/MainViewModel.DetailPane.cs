using CommunityToolkit.Mvvm.Input;

namespace Lignum.App.ViewModels;

// The detail pane (ticket 07): the layout is app-wide; the line shown belongs to each tab (LogTabViewModel.Detail).
public sealed partial class MainViewModel
{
    private DetailPaneLayoutViewModel? _detailPane;

    /// <summary>Open/collapsed, dock position and size of the detail pane, shared by all tabs and saved.</summary>
    public DetailPaneLayoutViewModel DetailPane => _detailPane ??= new DetailPaneLayoutViewModel(_settings);

    /// <summary>
    /// "Show full line": opens the detail pane on <paramref name="line"/> (0-based) of the selected tab, or on
    /// that tab's current line when null.
    /// </summary>
    [RelayCommand]
    public void ShowFullLine(long? line)
    {
        if (line is { } l && SelectedTab is { } tab) tab.Detail.CurrentLine = l;
        DetailPane.IsOpen = true;
    }
}
