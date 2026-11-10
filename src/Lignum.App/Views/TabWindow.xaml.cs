using System.Windows;
using System.Windows.Input;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

/// <summary>
/// A floating window of tabs (ticket 29). The main window opens one for each of
/// <see cref="MainViewModel.FloatingGroups"/> and closes it when its group goes. Closing it by hand docks
/// its tabs back into the main window.
/// </summary>
public partial class TabWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Func<KeyEventArgs, bool> _runShortcut;
    private bool _closingForGood;

    /// <param name="runShortcut">Runs the main window's keyboard shortcut for a key, if it has one (true: it ran).</param>
    public TabWindow(MainViewModel viewModel, TabGroupViewModel group, AppearanceViewModel appearance,
        Func<KeyEventArgs, bool> runShortcut)
    {
        _viewModel = viewModel;
        _runShortcut = runShortcut;
        Group = group;
        Appearance = appearance;
        DataContext = viewModel;
        InitializeComponent();
        Activated += (_, _) => _viewModel.ActivateGroup(Group);
        // Session restore: the group remembers where this window is.
        LocationChanged += (_, _) => RememberBounds();
        SizeChanged += (_, _) => RememberBounds();
        StateChanged += (_, _) => RememberBounds();
    }

    private void RememberBounds()
    {
        if (!IsLoaded || WindowState == WindowState.Minimized) return;
        var normal = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (normal.IsEmpty) return;
        Group.Bounds = new Settings.WindowBounds { Left = normal.Left, Top = normal.Top, Width = normal.Width, Height = normal.Height };
        Group.IsMaximized = WindowState == WindowState.Maximized;
    }

    /// <summary>This window's tabs.</summary>
    public TabGroupViewModel Group { get; }

    // The same names as MainWindow's: LogTabView binds them with RelativeSource AncestorType=Window.

    /// <summary>Theme, font, zoom and tab width.</summary>
    public AppearanceViewModel Appearance { get; }

    /// <summary>Line coloring rules (ticket 14).</summary>
    public HighlightingViewModel? Highlighting { get; init; }

    /// <summary>Syntax coloring by file type (ticket 25).</summary>
    public SyntaxViewModel? Syntax { get; init; }

    /// <summary>Closes the window without docking its tabs: its group is gone, or the app is closing.</summary>
    public void CloseForGood()
    {
        _closingForGood = true;
        if (!_closing) Close();
    }

    private bool _closing;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        _closing = true;
        if (!_closingForGood) _viewModel.CloseFloatingGroup(Group); // ✕ or Alt+F4: dock the tabs back
    }

    /// <summary>The main window's shortcuts (Ctrl+F, Ctrl+W, F3, …) work here too, on this window's tab.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && _runShortcut(e)) e.Handled = true;
    }

    /// <summary>Ctrl+mouse wheel zooms the text, as in the main window.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var command = e.Delta > 0 ? Appearance.ZoomInCommand : Appearance.ZoomOutCommand;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Files dropped here open in this window.</summary>
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        e.Handled = true;
        Activate();
        _viewModel.ActivateGroup(Group);
        _ = _viewModel.OpenDroppedAsync(paths);
    }
}
