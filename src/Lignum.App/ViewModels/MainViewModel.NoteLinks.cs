using System.ComponentModel;
using System.Diagnostics;

namespace Lignum.App.ViewModels;

// Ticket 23: Ctrl+click a web link in a bookmark note opens it in the default browser.
public sealed partial class MainViewModel
{
    internal void OpenNoteLink(NoteLink link)
    {
        try
        {
            StartProcess(new ProcessStartInfo(link.Target) { UseShellExecute = true });
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            ErrorMessage = $"Could not open {link.Target}: {ex.Message}";
        }
    }
}
