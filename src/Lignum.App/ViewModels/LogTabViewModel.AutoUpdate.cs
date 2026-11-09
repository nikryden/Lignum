using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>The small icon on a tab (ticket 27).</summary>
public enum TabIndicator
{
    None,

    /// <summary>The file changed and the tab has not caught up (the banner is showing).</summary>
    NeedsUpdate,

    /// <summary>Auto update is on and the tab is up to date.</summary>
    AutoUpdating,
}

/// <summary>Where the view goes when an auto-updating tab gets new lines.</summary>
public enum UpdatePosition
{
    /// <summary>Keep the view where it is, but follow the new lines when the last line was in view (like <c>tail -f</c>).</summary>
    StayInPlace,

    /// <summary>Move the caret to the last line on every update.</summary>
    JumpToEnd,
}

// Ticket 27: a tab can update by itself when its file changes. Growth indexes only the appended bytes;
// truncation or replacement reopens the file. With auto update off, the banner offers Reload instead.
public sealed partial class LogTabViewModel
{
    private int _viewportRows;

    /// <summary>Update the tab by itself when the file changes, instead of showing the banner.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Indicator))]
    private bool _autoUpdate;

    /// <summary>Where the view goes after an update (auto update or Reload).</summary>
    [ObservableProperty]
    private UpdatePosition _updatePosition;

    /// <summary>What the tab's icon shows: needing an update wins over auto updating (e.g. a reopen that failed).</summary>
    public TabIndicator Indicator =>
        IsFileChanged ? TabIndicator.NeedsUpdate : AutoUpdate ? TabIndicator.AutoUpdating : TabIndicator.None;

    /// <summary>Menu radio items: Jump to last line on update.</summary>
    public bool JumpsToEnd
    {
        get => UpdatePosition == UpdatePosition.JumpToEnd;
        set { if (value) UpdatePosition = UpdatePosition.JumpToEnd; }
    }

    /// <summary>Menu radio items: Stay in place on update.</summary>
    public bool StaysInPlace
    {
        get => UpdatePosition == UpdatePosition.StayInPlace;
        set { if (value) UpdatePosition = UpdatePosition.StayInPlace; }
    }

    partial void OnUpdatePositionChanged(UpdatePosition value)
    {
        OnPropertyChanged(nameof(JumpsToEnd));
        OnPropertyChanged(nameof(StaysInPlace));
    }

    /// <summary>Asks the view to scroll to the last row without moving the caret (Stay in place, following).</summary>
    public event EventHandler? ScrollToEndRequested;

    /// <summary>The view reports how many rows fit, so the tab knows whether the last line is in view.</summary>
    public void ReportViewportRows(int rows) => _viewportRows = Math.Max(0, rows);

    /// <summary>The scroll line and "last line in view" when the index became invalid; null otherwise.</summary>
    private (long Line, bool AtEnd)? _positionAtChange;

    /// <summary>True when the view shows the file's last line (or the whole file fits).</summary>
    private bool IsLastLineInView =>
        !HideNonMatchingLines && !IsScrollRestorePending && ScrollLine + Math.Max(1, _viewportRows) >= LineCount;

    partial void OnAutoUpdateChanged(bool value)
    {
        // Turning it on applies a change the banner is still offering.
        if (value && IsFileChanged) _ = Update();
    }

    /// <summary>Brings the tab up to date with the file: indexes the growth, or reopens a truncated/replaced file.</summary>
    private Task Update()
    {
        if (_document is not { } document) return Task.CompletedTask;
        return document.IsIndexValid ? IndexGrowth(document) : ReopenAndFollow();
    }

    private async Task IndexGrowth(LogDocument document)
    {
        var follow = UpdatePosition == UpdatePosition.JumpToEnd || IsLastLineInView;
        IsFileChanged = false;
        FileChangeMessage = "";

        var added = await document.IndexAppendedAsync().ConfigureAwait(false);
        RunOnUi(() =>
        {
            if (!ReferenceEquals(document, _document)) return;
            Refresh(document);
            FileSize = document.TotalBytes;
            if (added > 0) Search.MarkOutdated();
            if (added > 0 && follow) FollowEnd();
        });
    }

    private async Task ReopenAndFollow()
    {
        // Where the view was before the stale lines were cleared (see OnFileChanged).
        var (line, wasAtEnd) = _positionAtChange ?? (ScrollLine, IsLastLineInView);
        var follow = UpdatePosition == UpdatePosition.JumpToEnd || wasAtEnd;
        if (await OpenAgainAsync() is not { } document)
        {
            // Nothing there to read (deleted, or locked): offer Reload for when it comes back.
            FileChangeMessage = "The file was replaced or removed on disk.";
            IsFileChanged = true;
            return;
        }

        var completion = Attach(document);
        if (!follow) RestoreScroll(line);
        await completion.ConfigureAwait(false);
        if (follow) RunOnUi(() =>
        {
            if (ReferenceEquals(document, _document)) FollowEnd();
        });
    }

    private void FollowEnd()
    {
        if (UpdatePosition == UpdatePosition.JumpToEnd) Navigation.GoToEndCommand.Execute(null);
        else ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
    }
}
