using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Lignum.App.Settings;

/// <summary>
/// The folder that holds Lignum's user data as plain JSON files (never the registry):
/// <c>settings.json</c>, <c>highlight-presets.json</c>, <c>bookmarks/&lt;hash&gt;.json</c>.
/// The folder is passed in so tests can point it at a temp directory; the app uses
/// <see cref="DefaultDirectory"/>. Reading never throws: a missing or unreadable file gives
/// defaults and is logged. Writing never throws either: a failed save is logged.
/// </summary>
public sealed class SettingsStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger _log;

    public SettingsStore(string directory, ILogger log)
    {
        Directory = directory;
        _log = log;
    }

    /// <summary><c>%APPDATA%\Lignum</c> (roaming).</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lignum");

    public string Directory { get; }

    /// <summary>
    /// Loads <paramref name="relativePath"/> (e.g. <c>settings.json</c>, <c>bookmarks/x.json</c>) once.
    /// Mutate <see cref="SettingsFile{T}.Value"/> and call <see cref="SettingsFile{T}.Save"/>.
    /// Share one <see cref="SettingsFile{T}"/> per file across the app so saves don't overwrite each other.
    /// </summary>
    public SettingsFile<T> Open<T>(string relativePath) where T : class, new() =>
        new(this, relativePath, Load<T>(relativePath));

    internal string PathOf(string relativePath) => Path.Combine(Directory, relativePath);

    private T Load<T>(string relativePath) where T : class, new()
    {
        var path = PathOf(relativePath);
        if (!File.Exists(path))
        {
            _log.Information("No {File} in {Folder}; using defaults", relativePath, Directory);
            return new T();
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            _log.Warning(ex, "Could not read {Path}; using defaults", path);
            return new T();
        }
    }

    internal void Save<T>(string relativePath, T value)
    {
        var path = PathOf(relativePath);
        var temp = path + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Write a temp file and swap it in, so a crash mid-write never leaves a half-written file.
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, JsonOptions);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or JsonException)
        {
            _log.Warning(ex, "Could not save {Path}", path);
            try { File.Delete(temp); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }
}

/// <summary>One JSON file from a <see cref="SettingsStore"/>, loaded into <see cref="Value"/>.</summary>
public sealed class SettingsFile<T> where T : class
{
    private readonly SettingsStore _store;
    private readonly string _relativePath;
    private readonly Lock _saveLock = new();

    internal SettingsFile(SettingsStore store, string relativePath, T value)
    {
        _store = store;
        _relativePath = relativePath;
        Value = value;
    }

    public T Value { get; }

    /// <summary>Writes <see cref="Value"/> to disk now. Thread-safe; failures are logged, not thrown.</summary>
    public void Save()
    {
        lock (_saveLock) _store.Save(_relativePath, Value);
    }
}
