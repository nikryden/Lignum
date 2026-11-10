using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Lignum.App.ViewModels;
using Lignum.App.Views;

namespace Lignum.App;

// Floating windows of tabs (ticket 28). The view model says which groups exist and which is active; this
// file opens a TabWindow per floating group, closes it when the group goes, brings the active group's window
// to the front, and runs this window's keyboard shortcuts for keys pressed in a floating window.
public partial class MainWindow
{
    private readonly List<TabWindow> _tabWindows = [];

    private void InitializeTabWindows()
    {
        Activated += (_, _) => _viewModel.ActivateGroup(_viewModel.MainGroup);
        _viewModel.FloatingGroups.CollectionChanged += OnFloatingGroupsChanged;
        _viewModel.PropertyChanged += OnActiveGroupChanged;
    }

    /// <summary>The window that shows the active tab: dialogs for it (Go to line, Find) belong to it.</summary>
    private Window ActiveTabWindow =>
        (Window?)_tabWindows.FirstOrDefault(w => ReferenceEquals(w.Group, _viewModel.ActiveGroup)) ?? this;

    private void OnFloatingGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var window in _tabWindows.Where(w => !_viewModel.FloatingGroups.Contains(w.Group)).ToList())
        {
            _tabWindows.Remove(window);
            window.CloseForGood();
        }

        foreach (var group in _viewModel.FloatingGroups.Where(g => _tabWindows.All(w => !ReferenceEquals(w.Group, g))).ToList())
        {
            OpenTabWindow(group);
        }
    }

    /// <summary>Opens a window for a new floating group, under the mouse, a bit smaller than this window.</summary>
    private void OpenTabWindow(TabGroupViewModel group)
    {
        var window = new TabWindow(_viewModel, group, Appearance, RunShortcut)
        {
            Highlighting = Highlighting,
            Syntax = Syntax,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Width = Math.Max(400, RestoreBounds.Width * 0.7),
            Height = Math.Max(300, RestoreBounds.Height * 0.7),
        };
        var cursor = CursorInDips();
        window.Left = cursor.X - 60;
        window.Top = cursor.Y - 15;
        // Owned windows close with their owner: give the Find window back to this one first.
        window.Closing += (_, _) =>
        {
            if (ReferenceEquals(_findWindow?.Owner, window)) _findWindow!.Owner = this;
        };
        _tabWindows.Add(window);
        window.Show();
    }

    /// <summary>The mouse position on the screen, in device-independent units (window coordinates).</summary>
    private Point CursorInDips()
    {
        var device = PointToScreen(Mouse.GetPosition(this));
        return PresentationSource.FromVisual(this)?.CompositionTarget is { } target
            ? target.TransformFromDevice.Transform(device)
            : device;
    }

    /// <summary>A tab in another window became active (moved there, a search result, Go to note): bring that window to the front.</summary>
    private void OnActiveGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ActiveGroup)) return;
        Dispatcher.BeginInvoke(() =>
        {
            var window = ActiveTabWindow;
            if (window.IsActive || !window.IsLoaded) return;
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        });
    }

    /// <summary>The app is closing: close the floating windows without docking their tabs.</summary>
    private void CloseTabWindows()
    {
        foreach (var window in _tabWindows.ToList()) window.CloseForGood();
        _tabWindows.Clear();
    }

    /// <summary>
    /// Runs this window's shortcut for a key pressed in a floating window: its key bindings, then the gestures
    /// of its routed commands (Ctrl+O). Commands act on the active tab, which is that window's tab.
    /// </summary>
    private bool RunShortcut(KeyEventArgs e)
    {
        foreach (var binding in InputBindings.OfType<InputBinding>())
        {
            if (binding.Gesture?.Matches(this, e) == true && Run(binding.Command, binding.CommandParameter)) return true;
        }

        foreach (var binding in CommandBindings.OfType<CommandBinding>())
        {
            if (binding.Command is RoutedCommand command
                && command.InputGestures.OfType<InputGesture>().Any(g => g.Matches(this, e))
                && Run(command, null)) return true;
        }

        return false;
    }

    private bool Run(ICommand? command, object? parameter)
    {
        switch (command)
        {
            case RoutedCommand routed when routed.CanExecute(parameter, this):
                routed.Execute(parameter, this);
                return true;
            case RoutedCommand:
                return false;
            case not null when command.CanExecute(parameter):
                command.Execute(parameter);
                return true;
            default:
                return false;
        }
    }
}
