using Serilog;
using Serilog.Core;

namespace Lignum.App.Diagnostics;

/// <summary>
/// Lignum's own diagnostic log: a daily rolling Serilog file. Not to be confused with the log files the user views.
/// </summary>
public sealed class DiagnosticLog : IDisposable
{
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lignum", "logs");

    private DiagnosticLog(Logger logger) => Logger = logger;

    public ILogger Logger { get; }

    /// <summary>Creates the folder if needed, opens the rolling file and writes a startup entry.</summary>
    public static DiagnosticLog Start(string directory)
    {
        Directory.CreateDirectory(directory);
        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(directory, "lignum-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        var version = typeof(DiagnosticLog).Assembly.GetName().Version;
        logger.Information("Lignum starting, version {Version}, OS {OS}", version, Environment.OSVersion);
        return new DiagnosticLog(logger);
    }

    public void Dispose() => ((Logger)Logger).Dispose();
}
