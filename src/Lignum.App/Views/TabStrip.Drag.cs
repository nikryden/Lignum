using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

// Dragging tabs (ticket 28). A custom gesture with mouse capture, not OLE drag and drop: a drop outside every
// window has to open a new one, and the windows already take OLE drops of files from Explorer.
//   - Past the system drag distance, the strip captures the mouse and a ghost of the tab follows it.
//   - Over a tab strip (of the window on top under the mouse), a marker shows where the tab will land.
//   - Release over a strip: the tab moves there. Elsewhere: it opens in a new window under the mouse
//     (a tab alone in a floating window moves that window instead). Esc cancels.
public partial class TabStrip
{
    /// <summary>Every tab strip in a loaded window: the drop targets.</summary>
    private static readonly List<TabStrip> Strips = [];

    private LogTabViewModel? _pressedTab;
    private Point _pressPoint;
    private bool _dragging;
    private TabStrip? _markedStrip;
    private Window? _keyWindow;

    /// <summary>This strip's tabs (its DataContext).</summary>
    private TabGroupViewModel? Group => DataContext as TabGroupViewModel;

    private void InitializeDrag()
    {
        Loaded += (_, _) =>
        {
            if (!Strips.Contains(this)) Strips.Add(this);
        };
        Unloaded += (_, _) => Strips.Remove(this);
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedTab = null;
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<ButtonBase>(source) is not null) return; // the ✕ button
        if (FindAncestor<TabItem>(source) is not { DataContext: LogTabViewModel tab }) return;
        _pressedTab = tab;
        _pressPoint = e.GetPosition(this);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedTab is null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        if (!_dragging)
        {
            var moved = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            StartDrag();
        }

        UpdateDrag(PointToScreen(e.GetPosition(this)));
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            _pressedTab = null;
            return;
        }

        var tab = _pressedTab;
        var screen = PointToScreen(e.GetPosition(this));
        var target = FindTarget(screen);
        EndDrag();
        if (tab is not null) DropTab(tab, target, screen);
        e.Handled = true;
    }

    /// <summary>Another window or app took the mouse (Alt+Tab, a dialog): cancel.</summary>
    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragging) EndDrag();
    }

    private void StartDrag()
    {
        _dragging = true;
        CaptureMouse();
        GhostText.Text = _pressedTab?.FileName;
        Ghost.IsOpen = true;
        _keyWindow = Window.GetWindow(this);
        if (_keyWindow is not null) _keyWindow.PreviewKeyDown += OnDragKeyDown;
    }

    private void OnDragKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        EndDrag();
        e.Handled = true;
    }

    /// <summary>Stops the drag (dropped or cancelled): no capture, no ghost, no marker.</summary>
    private void EndDrag()
    {
        var wasDragging = _dragging;
        _dragging = false;
        _pressedTab = null;
        Ghost.IsOpen = false;
        _markedStrip?.HideMarker();
        _markedStrip = null;
        if (_keyWindow is not null) _keyWindow.PreviewKeyDown -= OnDragKeyDown;
        _keyWindow = null;
        if (wasDragging && IsMouseCaptured) ReleaseMouseCapture();
    }

    /// <summary>Moves the ghost to the mouse and the marker to the strip under it.</summary>
    private void UpdateDrag(Point screen)
    {
        var dips = ToDips(screen);
        // Below and right of the pointer, so the window under the pointer is not the ghost.
        Ghost.HorizontalOffset = dips.X + 14;
        Ghost.VerticalOffset = dips.Y + 18;

        var target = FindTarget(screen);
        if (!ReferenceEquals(_markedStrip, target?.Strip)) _markedStrip?.HideMarker();
        _markedStrip = target?.Strip;
        target?.Strip.ShowMarker(target.Value.Marker);
        GhostText.Text = target is null && IsAloneInFloatingWindow(_pressedTab) ? $"{_pressedTab?.FileName} (move window)" : _pressedTab?.FileName;
    }

    private void DropTab(LogTabViewModel tab, (TabStrip Strip, int Index, Rect Marker)? target, Point screen)
    {
        if (Window.GetWindow(this)?.DataContext is not MainViewModel main || main.GroupOf(tab) is not { } source) return;

        if (target is { Strip.Group: { } group } t)
        {
            var index = ReferenceEquals(group, source) ? TabDropMath.ReorderIndex(source.Tabs.IndexOf(tab), t.Index) : t.Index;
            main.MoveTab(tab, group, index);
            return;
        }

        if (IsAloneInFloatingWindow(tab))
        {
            // Already in its own window: move the window to where the tab was dropped.
            if (Window.GetWindow(this) is { } window)
            {
                var dips = ToDips(screen);
                window.Left = dips.X - 60;
                window.Top = dips.Y - 15;
            }
            return;
        }

        main.MoveToNewGroup(tab); // the main window opens it under the mouse
    }

    private bool IsAloneInFloatingWindow(LogTabViewModel? tab) =>
        tab is not null && Group is { IsMain: false } group && group.Tabs.Count == 1 && group.Tabs.Contains(tab);

    /// <summary>
    /// The strip a tab released at <paramref name="screen"/> (device pixels) would land in: the strip of the
    /// top window under the mouse, when the mouse is over its tabs (or anywhere in a window with no tabs,
    /// such as an empty main window). Null: outside every strip.
    /// </summary>
    private static (TabStrip Strip, int Index, Rect Marker)? FindTarget(Point screen)
    {
        var root = GetAncestor(WindowFromPoint(new NativePoint((int)screen.X, (int)screen.Y)), GaRoot);
        foreach (var strip in Strips)
        {
            if (Window.GetWindow(strip) is not { IsVisible: true } window || new WindowInteropHelper(window).Handle != root) continue;
            if (strip.Group is not { } group) continue;

            if (group.Tabs.Count == 0) return (strip, 0, Rect.Empty);
            var local = strip.PointFromScreen(screen);
            var zone = new Rect(0, -4, strip.ActualWidth, strip.Tabs.ActualHeight + 12); // a little slack above and below
            if (!zone.Contains(local)) return null;

            var bounds = strip.TabBounds();
            var index = TabDropMath.InsertIndex(bounds, local);
            return (strip, index, TabDropMath.MarkerBounds(bounds, index));
        }
        return null;
    }

    /// <summary>Each tab header's bounds in this strip, in order.</summary>
    private List<Rect> TabBounds()
    {
        var bounds = new List<Rect>();
        for (var i = 0; i < Tabs.Items.Count; i++)
        {
            if (Tabs.ItemContainerGenerator.ContainerFromIndex(i) is TabItem { IsVisible: true } item)
            {
                bounds.Add(item.TransformToAncestor(this).TransformBounds(new Rect(item.RenderSize)));
            }
        }
        return bounds;
    }

    private void ShowMarker(Rect bounds)
    {
        if (bounds.IsEmpty)
        {
            HideMarker();
            return;
        }
        Canvas.SetLeft(Marker, bounds.Left);
        Canvas.SetTop(Marker, bounds.Top);
        Marker.Height = bounds.Height;
        Marker.Visibility = Visibility.Visible;
    }

    private void HideMarker() => Marker.Visibility = Visibility.Collapsed;

    private Point ToDips(Point screen) =>
        PresentationSource.FromVisual(this)?.CompositionTarget is { } target ? target.TransformFromDevice.Transform(screen) : screen;

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null and not T)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return element as T;
    }

    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
