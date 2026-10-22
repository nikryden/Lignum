using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace Lignum.App.ViewModels;

// Ticket 12: the tab header's context menu (and the File menu) copy the file's path or show it in Explorer.
// For a package tab these use the .lm file the user opened, not the extracted log.
public sealed partial class LogTabViewModel
{
    [RelayCommand]
    private void CopyFullPath() => _services.Copier?.SetText(SourcePath);

    [RelayCommand]
    private void CopyFileName() => _services.Copier?.SetText(System.IO.Path.GetFileName(SourcePath));

    /// <summary>Opens Explorer on the file's folder with the file selected.</summary>
    [RelayCommand]
    private void OpenContainingFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{SourcePath}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Serilog.Log.Warning(ex, "Could not open the folder of {Path}", SourcePath);
        }
    }
}
