using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Lignum.App.Settings;

namespace Lignum.App.Views;

/// <summary>
/// Places <see cref="Main"/> and a side <see cref="Pane"/> (the detail pane) with a draggable splitter between
/// them: the pane docked right or at the bottom, or hidden when collapsed. Dragging writes the new size back
/// to <see cref="PaneWidth"/> / <see cref="PaneHeight"/> (bind them two-way to keep it).
/// </summary>
public sealed class DetailPaneLayout : Grid
{
    private const double SplitterSize = 5;

    public static readonly DependencyProperty MainProperty = DependencyProperty.Register(
        nameof(Main), typeof(UIElement), typeof(DetailPaneLayout),
        new PropertyMetadata(null, (d, e) => ((DetailPaneLayout)d).ReplaceChild(e)));

    public static readonly DependencyProperty PaneProperty = DependencyProperty.Register(
        nameof(Pane), typeof(UIElement), typeof(DetailPaneLayout),
        new PropertyMetadata(null, (d, e) => ((DetailPaneLayout)d).ReplaceChild(e)));

    public static readonly DependencyProperty DockProperty = DependencyProperty.Register(
        nameof(Dock), typeof(DetailPaneDock), typeof(DetailPaneLayout),
        new PropertyMetadata(DetailPaneDock.Right, OnLayoutChanged));

    public static readonly DependencyProperty IsPaneOpenProperty = DependencyProperty.Register(
        nameof(IsPaneOpen), typeof(bool), typeof(DetailPaneLayout),
        new PropertyMetadata(true, OnLayoutChanged));

    public static readonly DependencyProperty PaneWidthProperty = DependencyProperty.Register(
        nameof(PaneWidth), typeof(double), typeof(DetailPaneLayout),
        new FrameworkPropertyMetadata(DetailPaneSettings.DefaultWidth,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutChanged));

    public static readonly DependencyProperty PaneHeightProperty = DependencyProperty.Register(
        nameof(PaneHeight), typeof(double), typeof(DetailPaneLayout),
        new FrameworkPropertyMetadata(DetailPaneSettings.DefaultHeight,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutChanged));

    private readonly GridSplitter _splitter;

    public DetailPaneLayout()
    {
        _splitter = new GridSplitter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Focusable = false,
        };
        _splitter.SetResourceReference(BackgroundProperty, Themes.ThemeKeys.PaneBorder);
        _splitter.DragCompleted += OnSplitterDragCompleted;
        Children.Add(_splitter);
        UpdateLayoutDefinitions();
    }

    public UIElement? Main
    {
        get => (UIElement?)GetValue(MainProperty);
        set => SetValue(MainProperty, value);
    }

    public UIElement? Pane
    {
        get => (UIElement?)GetValue(PaneProperty);
        set => SetValue(PaneProperty, value);
    }

    public DetailPaneDock Dock
    {
        get => (DetailPaneDock)GetValue(DockProperty);
        set => SetValue(DockProperty, value);
    }

    public bool IsPaneOpen
    {
        get => (bool)GetValue(IsPaneOpenProperty);
        set => SetValue(IsPaneOpenProperty, value);
    }

    public double PaneWidth
    {
        get => (double)GetValue(PaneWidthProperty);
        set => SetValue(PaneWidthProperty, value);
    }

    public double PaneHeight
    {
        get => (double)GetValue(PaneHeightProperty);
        set => SetValue(PaneHeightProperty, value);
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DetailPaneLayout)d).UpdateLayoutDefinitions();

    private void ReplaceChild(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is UIElement old) Children.Remove(old);
        if (e.NewValue is UIElement added) Children.Add(added);
        UpdateLayoutDefinitions();
    }

    /// <summary>Main in cell 0, splitter in cell 1, pane in cell 2, along columns (right) or rows (bottom).</summary>
    private void UpdateLayoutDefinitions()
    {
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        var open = IsPaneOpen && Pane is not null;
        var right = Dock == DetailPaneDock.Right;

        if (open && right)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 80 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterSize) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PaneWidth), MinWidth = 80 });
        }
        else if (open)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 60 });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitterSize) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(PaneHeight), MinHeight = 60 });
        }

        _splitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (Pane is { } pane) pane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Place(Main, 0, right);
        Place(_splitter, 1, right);
        Place(Pane, 2, right);
    }

    private static void Place(UIElement? element, int cell, bool alongColumns)
    {
        if (element is null) return;
        SetColumn(element, alongColumns ? cell : 0);
        SetRow(element, alongColumns ? 0 : cell);
    }

    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (Pane is not FrameworkElement pane) return;
        if (Dock == DetailPaneDock.Right) SetCurrentValue(PaneWidthProperty, pane.ActualWidth);
        else SetCurrentValue(PaneHeightProperty, pane.ActualHeight);
    }
}
