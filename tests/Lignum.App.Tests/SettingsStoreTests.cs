using Lignum.App.Settings;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Lignum.App.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly CollectingSink _sink = new();
    private readonly ILogger _log;

    public SettingsStoreTests() =>
        _log = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();

    public void Dispose()
    {
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void Default_settings_folder_is_Lignum_under_roaming_app_data()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lignum");

        Assert.Equal(expected, SettingsStore.DefaultDirectory);
    }

    [Fact]
    public void Saved_settings_are_written_as_json_in_the_folder_and_load_back()
    {
        var file = new SettingsStore(_dir, _log).Open<AppSettings>(AppSettings.FileName);
        file.Value.Theme = ThemeChoice.Dark;
        file.Value.FontFamily = "Consolas";
        file.Value.FontSize = 15;
        file.Value.Zoom = 130;
        file.Value.TabWidth = 8;
        file.Save();

        SettingsStore.Flush(); // saves write in the background
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json")));
        var loaded = new SettingsStore(_dir, _log).Open<AppSettings>(AppSettings.FileName).Value;
        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
        Assert.Equal("Consolas", loaded.FontFamily);
        Assert.Equal(15, loaded.FontSize);
        Assert.Equal(130, loaded.Zoom);
        Assert.Equal(8, loaded.TabWidth);
    }

    [Fact]
    public void A_missing_file_gives_defaults_and_is_logged()
    {
        var settings = new SettingsStore(_dir, _log).Open<AppSettings>(AppSettings.FileName).Value;

        AssertDefaults(settings);
        Assert.Contains(_sink.Events, e => e.RenderMessage().Contains("settings.json"));
    }

    [Fact]
    public void A_corrupt_file_gives_defaults_and_logs_a_warning()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ \"theme\": \"Dark\", oops");

        var settings = new SettingsStore(_dir, _log).Open<AppSettings>(AppSettings.FileName).Value;

        AssertDefaults(settings);
        Assert.Contains(_sink.Events, e => e.Level == LogEventLevel.Warning && e.Exception is not null);
    }

    [Fact]
    public void A_file_with_the_wrong_shape_gives_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "[1, 2, 3]");

        var settings = new SettingsStore(_dir, _log).Open<AppSettings>(AppSettings.FileName).Value;

        AssertDefaults(settings);
        Assert.Contains(_sink.Events, e => e.Level == LogEventLevel.Warning);
    }

    [Fact]
    public void Settings_this_version_does_not_know_survive_a_save()
    {
        // A newer Lignum (or a later ticket) may have written fields this build does not model.
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "theme": "Light", "futureThing": { "a": [1, 2] } }""");

        var file = new SettingsStore(_dir, _log).Open<AppSettings>(AppSettings.FileName);
        file.Value.TabWidth = 2;
        file.Save();

        SettingsStore.Flush();
        var json = File.ReadAllText(path);
        Assert.Contains("futureThing", json);
        Assert.Contains("\"tabWidth\": 2", json);
    }

    [Fact]
    public void Files_in_sub_folders_are_supported()
    {
        // Bookmarks live in bookmarks/<hash>.json.
        var store = new SettingsStore(_dir, _log);
        var file = store.Open<AppSettings>(Path.Combine("bookmarks", "abc.json"));
        file.Value.TabWidth = 3;
        file.Save();

        Assert.Equal(3, store.Open<AppSettings>(Path.Combine("bookmarks", "abc.json")).Value.TabWidth);
    }

    [Fact]
    public void A_save_that_fails_is_logged_and_does_not_throw()
    {
        Directory.CreateDirectory(_dir);
        // A folder where the file should be makes the write fail.
        Directory.CreateDirectory(Path.Combine(_dir, "settings.json"));
        var file = new SettingsStore(_dir, _log).Open<AppSettings>(AppSettings.FileName);

        var ex = Record.Exception(file.Save);
        SettingsStore.Flush(); // the write runs in the background

        Assert.Null(ex);
        Assert.Contains(_sink.Events, e => e.Level >= LogEventLevel.Warning && e.Exception is not null);
    }

    private static void AssertDefaults(AppSettings s)
    {
        Assert.Equal(ThemeChoice.System, s.Theme);
        Assert.Equal("Cascadia Mono, Consolas", s.FontFamily);
        Assert.Equal(13, s.FontSize);
        Assert.Equal(100, s.Zoom);
        Assert.Equal(4, s.TabWidth);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get { lock (_events) return _events.ToList(); }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events) _events.Add(logEvent);
        }
    }
}
