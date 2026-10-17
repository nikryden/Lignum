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
        var commandLine = CommandLine.Parse(e.Args);
        _settingsStore = new SettingsStore(commandLine.SettingsDirectory ?? SettingsStore.DefaultDirectory, _log.Logger);
        _settings = _settingsStore.Open<AppSettings>(AppSettings.FileName);
        _systemTheme = new WindowsSystemTheme();
        var appearance = new AppearanceViewModel(_settings, _systemTheme);
        ThemeManager.Follow(this, appearance);

        // Line coloring rules and presets (ticket 14), in their own file, recolored on theme changes.
        var highlighting = new HighlightingViewModel(
            _settingsStore.Open<HighlightPresetsFile>(HighlightPresetsFile.FileName))
        {
            Theme = appearance.EffectiveTheme,
        };
        appearance.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppearanceViewModel.EffectiveTheme))
                highlighting.Theme = appearance.EffectiveTheme;
        };

        var viewModel = new MainViewModel(_settings) { Highlighting = highlighting };
        MainWindow = new MainWindow(viewModel, appearance) { Highlighting = highlighting };
        MainWindow.Show();

        // Reopen the last session's tabs (ticket 18) once the window has painted; files re-index in the
        // background. `Lignum.exe <path>` then opens (and selects) that file.
        var filePath = commandLine.FilePath;
        var logger = _log.Logger;
        MainWindow.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _ = viewModel.RestoreSession();
            if (filePath is not null)
            {
                logger.Information("Opening {Path} from the command line", filePath);
                _ = viewModel.OpenFileAsync(filePath);
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _systemTheme?.Dispose();
        _settings?.Save();
        SettingsStore.Flush();
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
