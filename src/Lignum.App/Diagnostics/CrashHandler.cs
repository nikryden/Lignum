using System.Text;
using Serilog;

namespace Lignum.App.Diagnostics;

/// <summary>Shows crash details to the user (the real one is a dialog that copies them to the clipboard).</summary>
public interface ICrashReporter
{
    void Show(string details);
}

/// <summary>
/// Turns an unhandled exception into a log entry and a user-facing report.
/// App wires it to the dispatcher, AppDomain and TaskScheduler unhandled-exception events.
/// </summary>
public sealed class CrashHandler(ILogger logger, ICrashReporter reporter)
{
    private int _showing;

    public void Handle(Exception exception, string source)
    {
        logger.Fatal(exception, "Unhandled exception on {Source}", source);

        // Several faults can arrive at once (e.g. a dispatcher fault and a task fault); show one dialog at a time.
        if (Interlocked.Exchange(ref _showing, 1) == 1) return;
        try
        {
            reporter.Show(BuildReport(exception, source));
        }
        catch (Exception reportFailure)
        {
            logger.Error(reportFailure, "Could not show the crash report");
        }
        finally
        {
            Interlocked.Exchange(ref _showing, 0);
        }
    }

    public static string BuildReport(Exception exception, string source)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Lignum {typeof(CrashHandler).Assembly.GetName().Version}");
        sb.AppendLine($"Time: {DateTimeOffset.Now:O}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"Runtime: {Environment.Version}");
        sb.AppendLine($"Source: {source}");
        sb.AppendLine();
        sb.Append(exception);
        return sb.ToString();
    }
}
