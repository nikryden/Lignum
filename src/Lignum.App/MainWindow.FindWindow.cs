using System.ComponentModel;
using Lignum.App.Settings;
using Lignum.App.ViewModels;
using Lignum.App.Views;

namespace Lignum.App;

// The Find window: Ctrl+F, the filter rules and Ctrl+Shift+F open it on their page. The view models still
// say what was asked for (IsFindBarOpen, IsFilterBarOpen, the focus requests); this file turns that into
// showing the window, and closing the window back into "closed".
public partial class MainWindow
{
    private FindWindow? _findWindow;
    private SearchViewModel? _watchedSearch;

    private void InitializeFindWindow()
    {
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab)) WatchSearch(_viewModel.SelectedTab?.Search);
        };
        _viewModel.AllTabsSearch.FindBoxFocusRequested += (_, _) => ShowFindWindow(FindPage.AllTabs);
        _viewModel.AllTabsSearch.FindInFilesRequested += (_, _) => ShowFindWindow(FindPage.Files);
        _viewModel.AllTabsSearch.ConfirmLargeSearch = message => System.Windows.MessageBox.Show(
            (System.Windows.Window?)_findWindow ?? this, message, "Find in files", System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.OK;
        WatchSearch(_viewModel.SelectedTab?.Search);
        Closed += (_, _) => _findWindow?.CloseForGood();
    }

    private void WatchSearch(SearchViewModel? search)
    {
        if (_watchedSearch is { } old)
        {
            old.FindBoxFocusRequested -= OnFindRequested;
            old.PropertyChanged -= OnSearchPropertyChanged;
        }

        _watchedSearch = search;
        if (search is null) return;
        search.FindBoxFocusRequested += OnFindRequested;
        search.PropertyChanged += OnSearchPropertyChanged;
    }

    private void OnFindRequested(object? sender, EventArgs e) => ShowFindWindow(FindPage.Find);

    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SearchViewModel search) return;
        switch (e.PropertyName)
        {
            case nameof(SearchViewModel.IsFindBarOpen):
                if (search.IsFindBarOpen) ShowFindWindow(FindPage.Find);
                else HideFindWindowOn(FindPage.Find); // Esc in the find box
                break;
            case nameof(SearchViewModel.IsFilterBarOpen):
                if (search.IsFilterBarOpen) ShowFindWindow(FindPage.Filter);
                else HideFindWindowOn(FindPage.Filter);
                break;
        }
    }

    private void ShowFindWindow(FindPage page)
    {
        if (_findWindow is null)
        {
            _findWindow = new FindWindow { Owner = this, DataContext = _viewModel };
            if (_viewModel.FindWindowBounds is { } bounds)
            {
                _findWindow.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                _findWindow.Left = bounds.Left;
                _findWindow.Top = bounds.Top;
                _findWindow.Width = bounds.Width;
                _findWindow.Height = bounds.Height;
            }
            else
            {
                _findWindow.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
            }

            _findWindow.IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is false) OnFindWindowHidden();
            };
        }

        _findWindow.ShowPage(page);
    }

    private void HideFindWindowOn(FindPage page)
    {
        if (_findWindow is { IsVisible: true } window && window.Page == page) window.Hide();
    }

    /// <summary>Remembers where the window was, marks the bars closed (so the next request reopens it) and returns to the log.</summary>
    private void OnFindWindowHidden()
    {
        if (_findWindow is { } window)
        {
            _viewModel.FindWindowBounds = new WindowBounds
            {
                Left = window.Left,
                Top = window.Top,
                Width = window.Width,
                Height = window.Height,
            };
        }

        foreach (var tab in _viewModel.Tabs)
        {
            tab.Search.IsFindBarOpen = false;
            tab.Search.IsFilterBarOpen = false;
        }

        Activate();
    }
}
