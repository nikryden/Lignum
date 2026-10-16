using System.Windows;
using System.Windows.Threading;
using Lignum.App.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Themes;
using Lignum.App.ViewModels;

namespace Lignum.App;

public partial class App : Application
{
    private DiagnosticLog? _log;
    private CrashHandler? _crashHandler;
    private SettingsStore? _settingsStore;
    private SettingsFile<AppSettings>? _settings;
    private WindowsSystemTheme? _systemTheme;

    protected override void OnStartup(StartupEventArgs e)
    {
        _log = DiagnosticLog.Start(DiagnosticLog.DefaultDirectory);
        _crashHandler = new CrashHandler(_log.Logger, new ClipboardCrashReporter());

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        // Settings (ticket 13). Later features share this one store and this one settings.json file.
        _settingsStore = new SettingsStore(SettingsStore.DefaultDirectory, _log.Logger);
        _settings = _settingsStore.Open<AppSettings>(AppSettings.FileName);
        _systemTheme = new WindowsSystemTheme();
        var appearance = new AppearanceViewModel(_settings, _systemTheme);
        ThemeManager.Follow(this, appearance);

        var viewModel = new MainViewModel();
        MainWindow = new MainWindow(viewModel, appearance);
        MainWindow.Show();

        // `Lignum.exe <path>` opens that file.
        if (e.Args.Length > 0)
        {
            _log.Logger.Information("Opening {Path} from the command line", e.Args[0]);
            _ = viewModel.OpenFileAsync(e.Args[0]);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _systemTheme?.Dispose();
        _settings?.Save();
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
