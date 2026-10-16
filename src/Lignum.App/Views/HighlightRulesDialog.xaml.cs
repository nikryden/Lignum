using System.Windows;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

/// <summary>Edits <see cref="HighlightingViewModel"/> rules and presets; every change applies at once.</summary>
public partial class HighlightRulesDialog : Window
{
    private readonly HighlightingViewModel _highlighting;

    public HighlightRulesDialog(HighlightingViewModel highlighting)
    {
        _highlighting = highlighting;
        DataContext = highlighting;
        InitializeComponent();
        PresetNameBox.Text = highlighting.ActivePreset?.Name ?? "";
        // Commit a cell being edited when the dialog closes, so the last edit isn't lost.
        Closing += (_, _) => RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
    }

    private void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        ShowPresetError(_highlighting.CreatePreset(PresetNameBox.Text) is null
            ? "Type a name that no other preset uses."
            : null);
    }

    private void OnRenameClick(object sender, RoutedEventArgs e) =>
        ShowPresetError(_highlighting.RenamePreset(PresetNameBox.Text)
            ? null
            : "Type a name that no other preset uses.");

    private void ShowPresetError(string? message)
    {
        PresetError.Text = message ?? "";
        PresetError.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
