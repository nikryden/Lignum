using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Lignum.App.Viewer;

namespace Lignum.App.Views;

/// <summary>A #RRGGBB text field with a palette popup; typing by hand still works. Empty means no color.</summary>
public partial class ColorField : UserControl
{
    // Rows: grays, strong colors, light backgrounds (light theme), dark backgrounds (dark theme), light text (dark theme).
    private static readonly string[] Colors =
    [
        "#000000", "#1E1E1E", "#404040", "#606060", "#808080", "#A0A0A0", "#C0C0C0", "#D4D4D4", "#E8E8E8", "#FFFFFF",
        "#A80000", "#C00000", "#E05000", "#B06000", "#8A4B00", "#008000", "#007070", "#0B5394", "#0050C0", "#8000A0",
        "#FDE7E9", "#FFCDD2", "#FFE0B2", "#FFF1DC", "#FFF59D", "#C8E6C9", "#B2EBF2", "#BBDEFB", "#E1BEE7", "#F8BBD0",
        "#4B1D1D", "#5A1E1E", "#4A2A12", "#3D2E12", "#3D3A12", "#1E3D1E", "#123D3D", "#1E3A5A", "#2A1E4A", "#4A1E3D",
        "#FF9A9A", "#FFB070", "#FFC870", "#FFE680", "#A8E6A0", "#80E0E0", "#8CC4FF", "#B0B8FF", "#D8A8FF", "#FFA8D8",
    ];

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(string), typeof(ColorField),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorPropertyChanged)
        {
            DefaultUpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });

    public ColorField()
    {
        InitializeComponent();
        // Detach the palette: a popup routes its mouse events to its parent, and in a DataGrid the cell handles
        // even handled clicks (focus, selection, mouse capture), which closed the popup before a color's Click.
        Root.Children.Remove(Palette);
        Palette.PlacementTarget = SwatchButton;
        PaletteItems.ItemsSource = Colors;
        Box.TextChanged += (_, _) => ShowSwatch(Box.Text);
        ShowSwatch(null);
    }

    /// <summary>The color text as typed or picked (#RRGGBB); empty or null for none.</summary>
    public string? Color
    {
        get => (string?)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>The text box, for focusing and selecting an invalid value.</summary>
    public TextBox TextBox => Box;

    private static void OnColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var field = (ColorField)d;
        var text = (string?)e.NewValue ?? "";
        if (field.Box.Text != text) field.Box.Text = text;
    }

    private void ShowSwatch(string? text) =>
        SwatchRect.Fill = LineHighlighter.ToBrush(text) ?? (Brush)Brushes.Transparent;

    private void Commit() => Color = Box.Text.Trim();

    private void OnBoxLostFocus(object sender, RoutedEventArgs e) => Commit();

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Commit(); // not handled: Enter still reaches the dialog's default button
    }

    private void OnSwatchButtonClick(object sender, RoutedEventArgs e)
    {
        Palette.IsOpen = true;
        Dispatcher.BeginInvoke(() => PaletteItems.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    private void Pick(string text)
    {
        Box.Text = text;
        Commit();
        Palette.IsOpen = false;
        Box.Focus();
    }

    private void OnPaletteColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color }) Pick(color);
    }

    private void OnNoneClick(object sender, RoutedEventArgs e) => Pick("");

    private void OnPaletteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Palette.IsOpen = false;
        SwatchButton.Focus();
        e.Handled = true;
    }
}
