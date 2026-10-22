using System.Windows;
using System.Windows.Controls;
using Lignum.App.Settings;
using Microsoft.Win32;

namespace Lignum.App.Views;

/// <summary>Edits <see cref="ExternalEditorSettings"/>; a preset fills in the command and arguments.</summary>
public partial class ExternalEditorDialog : Window
{
    public ExternalEditorDialog(ExternalEditorSettings current)
    {
        InitializeComponent();
        PresetBox.ItemsSource = ExternalEditor.Presets;
        CommandBox.Text = current.Command;
        ArgumentsBox.Text = current.Arguments;
        PresetBox.SelectedItem = ExternalEditor.Presets.FirstOrDefault(p =>
            string.Equals(p.Command, current.Command, StringComparison.OrdinalIgnoreCase) && p.Arguments == current.Arguments);
    }

    public string Command => CommandBox.Text;

    public string Arguments => ArgumentsBox.Text;

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is not EditorPreset preset) return;
        CommandBox.Text = preset.Command;
        ArgumentsBox.Text = preset.Arguments;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Programs (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            CommandBox.Text = dialog.FileName;
            PresetBox.SelectedItem = null;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CommandBox.Text))
        {
            CommandBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
