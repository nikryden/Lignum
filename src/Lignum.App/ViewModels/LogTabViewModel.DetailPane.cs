using System.ComponentModel;
using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

// The detail pane content (ticket 07): this tab's current line in full. It follows the caret (ticket 05);
// selection (08) can keep it that way, since the caret is one end of the selection.
public sealed partial class LogTabViewModel
{
    private DetailPaneViewModel? _detail;

    /// <summary>The line this tab's detail pane shows: the caret line, or one picked with "Show full line".</summary>
    public DetailPaneViewModel Detail
    {
        get
        {
            if (_detail is not null) return _detail;
            _detail = new DetailPaneViewModel { Reader = Lines?.Reader };
            Navigation.PropertyChanged += OnNavigationChangedForDetail;
            return _detail;
        }
    }

    partial void OnLinesChanged(LineSource? value) => Detail.Reader = value?.Reader;

    private void OnNavigationChangedForDetail(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationViewModel.Caret)) Detail.CurrentLine = Navigation.Caret.Line;
    }
}
