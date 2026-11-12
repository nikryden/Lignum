using System.Windows;
using System.Windows.Controls;
using Lignum.App.Settings;
using Microsoft.Win32;

namespace Lignum.App.Views;

/// <summary>Settings > External editor (ticket 30): edits <see cref="ExternalEditorSettings"/>; a preset fills in the fields.</summary>
public partial class ExternalEditorPage : UserControl
{
    private readonly Func<ExternalEditorSettings> _current;
    private readonly Action<string, string> _apply;

    public ExternalEditorPage(Func<ExternalEditorSettings> current, Action<string, string> apply)
    {
        _current = current;
        _apply = apply;
        InitializeComponent();
        PresetBox.ItemsSource = ExternalEditor.Presets;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        var current = _current();
        CommandBox.Text = current.Command;
        ArgumentsBox.Text = current.Arguments;
        PresetBox.SelectedItem = ExternalEditor.Presets.FirstOrDefault(p =>
            string.Equals(p.Command, current.Command, StringComparison.OrdinalIgnoreCase) && p.Arguments == current.Arguments);
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is not EditorPreset preset) return;
        CommandBox.Text = preset.Command;
        ArgumentsBox.Text = preset.Arguments;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Programs (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            CommandBox.Text = dialog.FileName;
            PresetBox.SelectedItem = null;
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CommandBox.Text))
        {
            Status.Text = "Type a command.";
            CommandBox.Focus();
            return;
        }

        _apply(CommandBox.Text, ArgumentsBox.Text);
        Status.Text = "Applied.";
    }

    private void OnRevertClick(object sender, RoutedEventArgs e)
    {
        ShowCurrent();
        Status.Text = "";
    }
}
