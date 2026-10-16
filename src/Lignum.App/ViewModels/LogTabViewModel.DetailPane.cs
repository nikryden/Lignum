using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

// The detail pane content (ticket 07): this tab's current line in full.
public sealed partial class LogTabViewModel
{
    /// <summary>The line this tab's detail pane shows. Selection/caret (08/05) set <c>Detail.CurrentLine</c>.</summary>
    public DetailPaneViewModel Detail { get; } = new();

    partial void OnLinesChanged(LineSource? value) => Detail.Reader = value?.Reader;
}
