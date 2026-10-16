using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

// The detail pane (ticket 07): it reads full lines from whatever the main view shows.
public sealed partial class MainViewModel
{
    /// <param name="detail">The detail pane (App passes one bound to settings.json); null creates an unsaved one.</param>
    public MainViewModel(DetailPaneViewModel? detail = null)
    {
        Detail = detail ?? new DetailPaneViewModel();
    }

    /// <summary>The detail pane: the current line in full. Selection/caret set <c>Detail.CurrentLine</c>.</summary>
    public DetailPaneViewModel Detail { get; }

    partial void OnLinesChanged(LineSource? value) => Detail.Reader = value?.Reader;
}
