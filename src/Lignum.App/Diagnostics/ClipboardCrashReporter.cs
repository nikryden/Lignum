using System.Windows;

namespace Lignum.App.Diagnostics;

/// <summary>Copies the crash details to the clipboard and tells the user in a friendly dialog.</summary>
public sealed class ClipboardCrashReporter : ICrashReporter
{
    public void Show(string details)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Show(details));
            return;
        }

        bool copied;
        try
        {
            Clipboard.SetText(details);
            copied = true;
        }
        catch (Exception)
        {
            // The clipboard can be held by another process; the details are still in the diagnostic log.
            copied = false;
        }

        var where = copied
            ? "The details have been copied to the clipboard, so you can paste them into a bug report."
            : $"The details could not be copied to the clipboard, but they are in the log folder:\n{DiagnosticLog.DefaultDirectory}";

        MessageBox.Show(
            $"Sorry, Lignum ran into an unexpected problem.\n\n{where}",
            "Lignum - unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
