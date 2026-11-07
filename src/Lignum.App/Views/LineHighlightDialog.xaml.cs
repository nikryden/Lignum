using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lignum.App.Viewer;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

/// <summary>Picks the hand-set colors of one or more lines (ticket 14); each field is typed or picked from a palette.</summary>
public partial class LineHighlightDialog : Window
{
    private const string DefaultBackground = "#FFF59D";

    public LineHighlightDialog(long lineCount, LineHighlight? current)
    {
        InitializeComponent();
        Prompt.Text = lineCount == 1 ? "Colors for this line" : $"Colors for {lineCount:N0} lines";
        ForegroundField.Color = current?.Foreground ?? "";
        BackgroundField.Color = current?.Background ?? (current is null ? DefaultBackground : "");
        Loaded += (_, _) => BackgroundField.TextBox.Focus();
    }

    public string LineForeground => ForegroundField.TextBox.Text.Trim();

    public string LineBackground => BackgroundField.TextBox.Text.Trim();

    private void OnColorChanged(object sender, TextChangedEventArgs e)
    {
        if (Sample is null || ForegroundField is null || BackgroundField is null) return;
        Sample.Foreground = LineHighlighter.ToBrush(ForegroundField.TextBox.Text) ?? Foreground;
        Sample.Background = LineHighlighter.ToBrush(BackgroundField.TextBox.Text) ?? Brushes.Transparent;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        foreach (var box in new[] { ForegroundField.TextBox, BackgroundField.TextBox })
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
