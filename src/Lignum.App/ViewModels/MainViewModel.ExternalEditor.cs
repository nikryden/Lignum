using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Editors = Lignum.App.Settings.ExternalEditor;

namespace Lignum.App.ViewModels;

// Ticket 13: open the selected tab's log in the user's text editor, at the caret line. Lignum only starts the editor;
// it never writes to the log itself.
public sealed partial class MainViewModel
{
    /// <summary>The editor "Open in editor" starts.</summary>
    public ExternalEditorSettings ExternalEditor => _settings.Value.ExternalEditor;

    /// <summary>Lignum's settings folder; AI file tools never write there (it holds the AI permissions).</summary>
    internal string SettingsFolder => _settings.Store.Directory;

    /// <summary>Starts a process; tests replace it.</summary>
    internal Action<ProcessStartInfo> StartProcess { get; set; } = info => Process.Start(info)?.Dispose();

    public void SetExternalEditor(string command, string arguments)
    {
        _settings.Value.ExternalEditor = new ExternalEditorSettings { Command = command.Trim(), Arguments = arguments.Trim() };
        _settings.Save();
        OnPropertyChanged(nameof(ExternalEditor));
    }

    /// <summary>Opens the selected tab's log in the editor at <paramref name="line"/> (0-based), or at the caret line.</summary>
    [RelayCommand]
    private void OpenInEditor(long? line)
    {
        if (SelectedTab is not { } tab) return;
        var editor = ExternalEditor;
        var command = string.IsNullOrWhiteSpace(editor.Command) ? Editors.Notepad.Command : editor.Command;
        var oneBased = (line ?? tab.Navigation.Caret.Line) + 1;
        try
        {
            // Shell execute finds editors registered under App Paths (notepad++.exe) and .cmd shims on PATH (code).
            StartProcess(new ProcessStartInfo(command, Editors.ExpandArguments(editor.Arguments, tab.Path, oneBased))
            {
                UseShellExecute = true,
            });
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            ErrorMessage = $"Could not start the editor \"{command}\": {ex.Message}. Change it under View → External editor…";
        }
    }
}
