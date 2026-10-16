using System.Windows;
using System.Windows.Threading;
using Lignum.App.Diagnostics;

namespace Lignum.App;

public partial class App : Application
{
    private DiagnosticLog? _log;
    private CrashHandler? _crashHandler;

    protected override void OnStartup(StartupEventArgs e)
    {
        _log = DiagnosticLog.Start(DiagnosticLog.DefaultDirectory);
        _crashHandler = new CrashHandler(_log.Logger, new ClipboardCrashReporter());

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Logger.Information("Lignum exiting with code {ExitCode}", e.ApplicationExitCode);
        _log?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _crashHandler?.Handle(e.Exception, "UI thread");
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new Exception($"Non-exception thrown: {e.ExceptionObject}");
        _crashHandler?.Handle(exception, e.IsTerminating ? "background thread (terminating)" : "background thread");
        if (e.IsTerminating) _log?.Dispose();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _crashHandler?.Handle(e.Exception, "background task");
        e.SetObserved();
    }
}
