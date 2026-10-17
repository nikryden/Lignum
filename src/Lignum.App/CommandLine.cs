namespace Lignum.App;

/// <summary>
/// <c>Lignum.exe [--settings-dir &lt;folder&gt;] [file]</c>. <c>--settings-dir</c> keeps settings, bookmarks and
/// the session in another folder (to try things or run tests without touching your own); the default is
/// <see cref="Settings.SettingsStore.DefaultDirectory"/>.
/// </summary>
internal sealed record CommandLine(string? SettingsDirectory, string? FilePath)
{
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        string? settingsDirectory = null;
        string? filePath = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "--settings-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                settingsDirectory = args[++i];
            else
                filePath ??= args[i];
        }

        return new CommandLine(settingsDirectory, filePath);
    }
}
