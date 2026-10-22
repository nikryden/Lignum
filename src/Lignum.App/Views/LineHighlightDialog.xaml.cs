using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lignum.App.Viewer;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

/// <summary>Picks the hand-set colors of one or more lines (ticket 14); a swatch fills in its color.</summary>
public partial class LineHighlightDialog : Window
{
    private static readonly string[] ForegroundColors = ["#000000", "#FFFFFF", "#C00000", "#0050C0", "#008000", "#8000A0", "#B06000"];

    private static readonly string[] BackgroundColors = ["#FFF59D", "#FFCDD2", "#C8E6C9", "#BBDEFB", "#E1BEE7", "#FFE0B2", "#5A1E1E", "#1E3A5A"];

    public LineHighlightDialog(long lineCount, LineHighlight? current)
    {
        InitializeComponent();
        Prompt.Text = lineCount == 1 ? "Colors for this line" : $"Colors for {lineCount:N0} lines";
        ForegroundSwatches.ItemsSource = ForegroundColors;
        BackgroundSwatches.ItemsSource = BackgroundColors;
        ForegroundBox.Text = current?.Foreground ?? "";
        BackgroundBox.Text = current?.Background ?? (current is null ? BackgroundColors[0] : "");
        Loaded += (_, _) => BackgroundBox.Focus();
    }

    public string LineForeground => ForegroundBox.Text.Trim();

    public string LineBackground => BackgroundBox.Text.Trim();

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color } button) return;
        (IsIn(button, ForegroundSwatches) ? ForegroundBox : BackgroundBox).Text = color;
    }

    private static bool IsIn(DependencyObject child, DependencyObject parent)
    {
        for (var node = child; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, parent)) return true;
        }

        return false;
    }

    private void OnColorChanged(object sender, TextChangedEventArgs e)
    {
        if (Sample is null) return;
        Sample.Foreground = LineHighlighter.ToBrush(ForegroundBox?.Text) ?? Foreground;
        Sample.Background = LineHighlighter.ToBrush(BackgroundBox?.Text) ?? Brushes.Transparent;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        foreach (var box in new[] { ForegroundBox, BackgroundBox })
        {
            if (box.Text.Trim().Length > 0 && LineHighlighter.ParseColor(box.Text) is null)
            {
                box.Focus();
                box.SelectAll();
                return;
            }
        }

        DialogResult = true;
    }
}
