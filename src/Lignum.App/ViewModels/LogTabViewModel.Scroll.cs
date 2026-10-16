using CommunityToolkit.Mvvm.ComponentModel;

namespace Lignum.App.ViewModels;

// The viewer's scroll position, remembered for session restore (ticket 18). The view reports where it is
// scrolled; a restored tab asks the view to scroll back once enough lines have been indexed.
public sealed partial class LogTabViewModel
{
    /// <summary>The 0-based file line at the top of the viewer (saved in the session).</summary>
    [ObservableProperty]
    private long _scrollLine;

    /// <summary>
    /// True after a restore until the view has scrolled to <see cref="ScrollLine"/> (or given up because
    /// the file got shorter). Meanwhile the view's own position, which starts at the top, is not recorded.
    /// </summary>
    [ObservableProperty]
    private bool _isScrollRestorePending;

    /// <summary>The view scrolled: record its first line (ignored while a restore is pending).</summary>
    public void ReportScroll(long firstLine)
    {
        if (!IsScrollRestorePending) ScrollLine = Math.Max(0, firstLine);
    }

    /// <summary>The view reached the restored line, or cannot: record its scrolling again.</summary>
    public void CompleteScrollRestore() => IsScrollRestorePending = false;

    /// <summary>Session restore: the view should scroll to <paramref name="line"/> once it can.</summary>
    internal void RestoreScroll(long line)
    {
        if (line <= 0) return;
        ScrollLine = line;
        IsScrollRestorePending = true;
    }
}
