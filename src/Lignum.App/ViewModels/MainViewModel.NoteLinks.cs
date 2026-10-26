using System.ComponentModel;
using System.Diagnostics;

namespace Lignum.App.ViewModels;

// Ticket 23: Ctrl+click a link in a bookmark note. A web address opens in the default browser, a file in a tab, a
// folder in Explorer. A path is a link when it looks like one, so a missing target is reported only when clicked.
public sealed partial class MainViewModel
{
    /// <summary>Opens <paramref name="link"/>; with <paramref name="inFolder"/>, shows it in Explorer instead.</summary>
    internal async Task OpenNoteLinkAsync(NoteLink link, bool inFolder = false)
    {
        if (link.Kind == NoteLinkKind.Web)
        {
            StartLinkProcess(new ProcessStartInfo(link.Target) { UseShellExecute = true }, link.Target);
            return;
        }

        // Off the UI thread: an unreachable share can take seconds to answer.
        var (isFile, isFolder) = await Task.Run(() => (File.Exists(link.Target), Directory.Exists(link.Target)));
        if (!isFile && !isFolder)
        {
            ErrorMessage = $"{link.Target} does not exist.";
            return;
        }

        if (inFolder)
            StartLinkProcess(new ProcessStartInfo("explorer.exe", $"/select,\"{link.Target}\"") { UseShellExecute = true }, link.Target);
        else if (isFolder)
            StartLinkProcess(new ProcessStartInfo(link.Target) { UseShellExecute = true }, link.Target);
        else
            await OpenFileAsync(link.Target);
    }

    private void StartLinkProcess(ProcessStartInfo info, string target)
    {
        try
        {
            StartProcess(info);
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            ErrorMessage = $"Could not open {target}: {ex.Message}";
        }
    }
}
