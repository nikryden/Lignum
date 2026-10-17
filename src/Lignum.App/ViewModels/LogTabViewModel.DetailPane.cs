using System.ComponentModel;
using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

// The detail pane content (ticket 07): this tab's current line in full. It follows the caret (ticket 05) and,
// when the selection (08) spans several lines, shows all of them (only the rows the main view shows, unless the
// app-wide "include hidden lines" setting is on).
public sealed partial class LogTabViewModel
{
    private DetailPaneViewModel? _detail;

    /// <summary>The line this tab's detail pane shows: the caret line, or one picked with "Show full line".</summary>
    public DetailPaneViewModel Detail
    {
        get
        {
            if (_detail is not null) return _detail;
            _detail = new DetailPaneViewModel { Reader = Lines?.Reader, ShownLines = HidingView?.Lines };
            Navigation.PropertyChanged += OnNavigationChangedForDetail;
            Selection.Changed += (_, _) => UpdateDetailFromSelection();
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ViewLines)) _detail.ShownLines = HidingView?.Lines;
            };
            return _detail;
        }
    }

    partial void OnLinesChanged(LineSource? value) => Detail.Reader = value?.Reader;

    private void OnNavigationChangedForDetail(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationViewModel.Caret)) UpdateDetailFromSelection();
    }

    /// <summary>The caret line, or every selected line when the selection spans several.</summary>
    private void UpdateDetailFromSelection()
    {
        var selection = Selection;
        if (selection.IsEmpty || selection.Start.Line == selection.End.Line || selection.Caret != Navigation.Caret)
        {
            Detail.CurrentLine = Navigation.Caret.Line;
            return;
        }

        // A selection ending at column 0 does not take in that last line (as in most editors).
        var last = selection.End.Column == 0 ? selection.End.Line - 1 : selection.End.Line;
        Detail.ShowRange(selection.Start.Line, last);
    }
}
