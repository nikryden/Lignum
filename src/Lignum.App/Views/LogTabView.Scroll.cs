using System.ComponentModel;
using System.Windows;
using Lignum.App.ViewModels;
using Lignum.App.Viewer;

namespace Lignum.App.Views;

// Scroll position for session restore (ticket 18): the viewer's first row is reported to the tab, and a
// restored tab's saved line is applied once the viewer has a size and enough lines are indexed.
public partial class LogTabView
{
    private static readonly DependencyPropertyDescriptor FirstRowDescriptor =
        DependencyPropertyDescriptor.FromProperty(LogView.FirstRowProperty, typeof(LogView));

    private LogTabViewModel? _scrollTab;
    private bool _applyingRestore;

    private void InitializeScroll()
    {
        // The descriptor holds the view strongly: subscribe only while it is in the window.
        // RemoveValueChanged first: WPF can raise Loaded twice without an Unloaded in between.
        Loaded += (_, _) =>
        {
            FirstRowDescriptor.RemoveValueChanged(LogView, OnFirstRowChanged);
            FirstRowDescriptor.AddValueChanged(LogView, OnFirstRowChanged);
        };
        Unloaded += (_, _) => FirstRowDescriptor.RemoveValueChanged(LogView, OnFirstRowChanged);
        LogView.SizeChanged += (_, _) => TryRestoreScroll();
    }

    private void AttachScroll(LogTabViewModel? newTab)
    {
        if (_scrollTab is not null) _scrollTab.PropertyChanged -= OnScrollTabPropertyChanged;
        _scrollTab = newTab;
        if (newTab is not null) newTab.PropertyChanged += OnScrollTabPropertyChanged;
        TryRestoreScroll();
    }

    private void OnScrollTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogTabViewModel.LineCount) or nameof(LogTabViewModel.IsIndexing)
            or nameof(LogTabViewModel.Lines))
        {
            TryRestoreScroll();
        }
    }

    private void OnFirstRowChanged(object? sender, EventArgs e)
    {
        if (_scrollTab is not { } tab || _applyingRestore) return;
        // The user scrolled (or the view reset) before the restore finished: their position wins.
        tab.CompleteScrollRestore();
        tab.ReportScroll(LogView.FirstRow);
    }

    /// <summary>Scrolls to the restored line; done when it is reached or indexing has stopped short of it.</summary>
    private void TryRestoreScroll()
    {
        if (_scrollTab is not { IsScrollRestorePending: true } tab) return;
        if (LogView.Source is null || LogView.ViewportRows <= 0) return; // not laid out (hidden tab) yet

        var target = tab.ScrollLine;
        _applyingRestore = true;
        try
        {
            LogView.SetCurrentValue(LogView.FirstRowProperty, target); // coerced to what is indexed so far
        }
        finally
        {
            _applyingRestore = false;
        }

        if (LogView.FirstRow >= target || !tab.IsIndexing)
        {
            tab.CompleteScrollRestore();
            tab.ReportScroll(LogView.FirstRow);
        }
    }
}
