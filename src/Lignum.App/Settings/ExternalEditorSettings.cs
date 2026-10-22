using System.Globalization;

namespace Lignum.App.Settings;

/// <summary>
/// The text editor "Open in editor" starts (ticket 13). <see cref="Arguments"/> is a template: <c>{file}</c> becomes
/// the log's full path and <c>{line}</c> the 1-based caret line.
/// </summary>
public sealed class ExternalEditorSettings
{
    public string Command { get; set; } = ExternalEditor.Notepad.Command;

    public string Arguments { get; set; } = ExternalEditor.Notepad.Arguments;
}

/// <summary>A known editor with the arguments that open a file at a line.</summary>
public sealed record EditorPreset(string Name, string Command, string Arguments)
{
    public override string ToString() => Name;
}

public static class ExternalEditor
{
    public static readonly EditorPreset Notepad = new("Notepad", "notepad.exe", "\"{file}\"");

    public static IReadOnlyList<EditorPreset> Presets { get; } =
    [
        Notepad,
        new("Notepad++", "notepad++.exe", "-n{line} \"{file}\""),
        new("Visual Studio Code", "code", "-g \"{file}:{line}\""),
        new("Sublime Text", "subl", "\"{file}:{line}\""),
    ];

    /// <summary>The argument string for <paramref name="file"/> at 1-based <paramref name="line"/>.</summary>
    public static string ExpandArguments(string template, string file, long line) =>
        (string.IsNullOrWhiteSpace(template) ? "\"{file}\"" : template)
            .Replace("{file}", file, StringComparison.OrdinalIgnoreCase)
            .Replace("{line}", line.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
}
