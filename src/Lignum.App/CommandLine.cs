namespace Lignum.App;

/// <summary>
/// <c>Lignum.exe [--settings-dir &lt;folder&gt;] [--mcp] [file]</c>. <c>--mcp</c> runs no window: it is the MCP server an AI tool starts, relaying to the running Lignum (ticket 05). <c>--settings-dir</c> keeps settings, bookmarks and
/// the session in another folder (to try things or run tests without touching your own); the default is
/// <see cref="Settings.SettingsStore.DefaultDirectory"/>.
/// </summary>
internal sealed record CommandLine(string? SettingsDirectory, string? FilePath, bool IsMcp = false)
{
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        string? settingsDirectory = null;
        string? filePath = null;
        var isMcp = false;
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "--settings-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                settingsDirectory = args[++i];
            else if (string.Equals(args[i], "--mcp", StringComparison.OrdinalIgnoreCase))
                isMcp = true;
            else
                filePath ??= args[i];
        }

        return new CommandLine(settingsDirectory, filePath, isMcp);
    }
}
