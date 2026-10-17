using Lignum.App.Settings;
using Lignum.App.Diagnostics;

namespace Lignum.App.Tests;

public sealed class CrashHandlerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed class RecordingReporter : ICrashReporter
    {
        public List<string> Reports { get; } = [];
        public void Show(string details) => Reports.Add(details);
    }

    private static Exception Thrown()
    {
        try { throw new InvalidOperationException("index went sideways"); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void An_unhandled_exception_is_shown_with_its_type_message_source_and_stack()
    {
        var reporter = new RecordingReporter();
        using var log = DiagnosticLog.Start(_dir);
        var handler = new CrashHandler(log.Logger, reporter);

        handler.Handle(Thrown(), "background task");

        var report = Assert.Single(reporter.Reports);
        Assert.Contains("System.InvalidOperationException", report);
        Assert.Contains("index went sideways", report);
        Assert.Contains("background task", report);
        Assert.Contains(nameof(Thrown), report);
    }

    [Fact]
    public void An_unhandled_exception_is_written_to_the_diagnostic_log()
    {
        using (var log = DiagnosticLog.Start(_dir))
        {
            new CrashHandler(log.Logger, new RecordingReporter()).Handle(Thrown(), "UI thread");
        }

        var text = DiagnosticsTests.ReadShared(Directory.GetFiles(_dir, "lignum-*.log").Single());
        Assert.Contains("index went sideways", text);
        Assert.Contains("UI thread", text);
    }

    [Fact]
    public void A_failing_dialog_does_not_throw_out_of_the_handler()
    {
        using var log = DiagnosticLog.Start(_dir);
        var handler = new CrashHandler(log.Logger, new ThrowingReporter());

        handler.Handle(Thrown(), "UI thread");
    }

    private sealed class ThrowingReporter : ICrashReporter
    {
        public void Show(string details) => throw new InvalidOperationException("clipboard busy");
    }
}
