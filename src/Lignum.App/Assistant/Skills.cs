using System.Text;
using System.Text.RegularExpressions;

namespace Lignum.App.Assistant;

/// <summary>
/// Where an entry in the "/" list comes from: the user's own skills (every log, or the selected log's folder),
/// skills shipped with Lignum, the agent's own skills, or a chat command Lignum runs itself.
/// </summary>
public enum SkillScope
{
    Global,
    Local,
    BuiltIn,
    Agent,
    Command,
}

/// <summary>
/// A skill: instructions an AI agent follows when the user starts a message with <c>/name</c>. Same format as
/// Claude Code skills: a folder with a <c>SKILL.md</c> whose front matter gives <c>name:</c> and
/// <c>description:</c>, then the instructions.
/// </summary>
/// <param name="FilePath">The SKILL.md; empty for a chat command and a built-in skill.</param>
/// <param name="Text">A built-in skill's whole SKILL.md (it is not a file on disk).</param>
/// <param name="Owner">For an agent's own skill, the agent's name ("Claude Code").</param>
public sealed record Skill(string Name, string Description, SkillScope Scope, string FilePath, string? Text = null, string? Owner = null)
{
    /// <summary>"global", "local", "built-in", "command" or the agent's name, as the suggestion list shows it.</summary>
    public string ScopeText => Scope switch
    {
        SkillScope.Local => "local",
        SkillScope.BuiltIn => "built-in",
        SkillScope.Agent => Owner ?? "agent",
        SkillScope.Command => "command",
        _ => "global",
    };

    /// <summary>The instructions (the file after its front matter), read fresh so edits apply at once.</summary>
    public string ReadInstructions() => SkillLibrary.Parse(Text ?? File.ReadAllText(FilePath), "").Body;
}

/// <summary>
/// Finds skills: global ones in <c>%APPDATA%\Lignum\skills\&lt;name&gt;\SKILL.md</c>, local ones in
/// <c>.lignum\skills\&lt;name&gt;\SKILL.md</c> next to the selected log. A local skill hides a global one of the same
/// name. The AI never gets the text in the prompt itself (command lines are short and one line for some agents):
/// the prompt names the skill and the agent fetches it with Lignum's <c>get_skill</c> tool.
/// </summary>
public static partial class SkillLibrary
{
    public const string FileName = "SKILL.md";

    /// <summary>Longest instructions handed to an agent; the rest is cut with a note.</summary>
    public const int MaxInstructionChars = 50_000;

    public static string DefaultGlobalFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lignum", "skills");

    /// <summary>The local skills folder for a log file: <c>.lignum\skills</c> in its folder; null without a log.</summary>
    public static string? LocalFolderFor(string? logPath) =>
        string.IsNullOrEmpty(logPath) || Path.GetDirectoryName(Path.GetFullPath(logPath)) is not { } folder
            ? null
            : Path.Combine(folder, ".lignum", "skills");

    /// <summary>
    /// Every skill in <paramref name="globalFolder"/> and <paramref name="localFolder"/>, by name; local wins over
    /// global. With <paramref name="builtIn"/>, Lignum's own skills come too, and the user's skills win over them.
    /// </summary>
    public static IReadOnlyList<Skill> Load(string globalFolder, string? localFolder, bool builtIn = false)
    {
        var byName = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
        if (builtIn)
        {
            foreach (var skill in BuiltIn) byName[skill.Name] = skill;
        }

        foreach (var skill in LoadFolder(globalFolder, SkillScope.Global)) byName[skill.Name] = skill;
        if (localFolder is not null)
        {
            foreach (var skill in LoadFolder(localFolder, SkillScope.Local)) byName[skill.Name] = skill;
        }

        return [.. byName.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The skills shipped inside Lignum.exe (Assistant\BuiltInSkills\&lt;name&gt;\SKILL.md, embedded).</summary>
    public static IReadOnlyList<Skill> BuiltIn { get; } = LoadBuiltIn();

    private static List<Skill> LoadBuiltIn()
    {
        var assembly = typeof(SkillLibrary).Assembly;
        var skills = new List<Skill>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("BuiltInSkills/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            var parsed = Parse(text, resource["BuiltInSkills/".Length..].Trim('/', Path.DirectorySeparatorChar));
            if (parsed.Name.Length > 0) skills.Add(new Skill(parsed.Name, parsed.Description, SkillScope.BuiltIn, "", text));
        }

        return [.. skills.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<Skill> LoadFolder(string folder, SkillScope scope)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(folder)) yield break;
            files = Directory.GetFiles(folder, FileName, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1 });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var folderName = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";
            var parsed = Parse(text, folderName);
            if (parsed.Name.Length > 0) yield return new Skill(parsed.Name, parsed.Description, scope, file);
        }
    }

    /// <summary>
    /// Splits a SKILL.md into its name (front matter <c>name:</c>, else <paramref name="fallbackName"/>), its
    /// description and the body. Names are made safe to type: lower case, spaces become dashes.
    /// </summary>
    internal static (string Name, string Description, string Body) Parse(string text, string fallbackName)
    {
        string? name = null, description = null;
        var body = text;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
            if (end > 0)
            {
                foreach (var line in lines[1..end])
                {
                    var colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    var key = line[..colon].Trim();
                    var value = line[(colon + 1)..].Trim().Trim('"', '\'');
                    if (key.Equals("name", StringComparison.OrdinalIgnoreCase)) name = value;
                    else if (key.Equals("description", StringComparison.OrdinalIgnoreCase)) description = value;
                }

                body = string.Join('\n', lines[(end + 1)..]).Trim();
            }
        }

        return (Normalize(name ?? fallbackName), description ?? "", body);
    }

    /// <summary>"Find Errors" → "find-errors"; only letters, digits, '-' and '_'.</summary>
    public static string Normalize(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_') builder.Append(c);
            else if (char.IsWhiteSpace(c) && builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>
    /// "/name text" → the skill's name and the rest of the message; null when the message does not start with
    /// a slash command.
    /// </summary>
    public static (string Name, string Text)? ParseCommand(string message)
    {
        var match = CommandPattern().Match(message);
        return match.Success ? (match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value.Trim()) : null;
    }

    /// <summary>The prompt sent for <c>/name rest</c>: the agent is told to fetch the skill with get_skill first.</summary>
    public static string PromptFor(Skill skill, string rest) =>
        $"The user chose the skill \"{skill.Name}\". Before anything else, call Lignum's get_skill tool with name " +
        $"\"{skill.Name}\" and follow the instructions it returns for this request. " +
        (rest.Length > 0 ? $"The user's request: {rest}" : "The user gave no further text: do what the skill describes.");

    /// <summary>
    /// Writes <c>&lt;folder&gt;\&lt;name&gt;\SKILL.md</c> with front matter and <paramref name="instructions"/>. The
    /// name is normalized, so the file always lands directly under <paramref name="folder"/>. Returns the file, or
    /// throws <see cref="InvalidOperationException"/> when the skill exists and <paramref name="overwrite"/> is false.
    /// </summary>
    public static string Save(string folder, string name, string description, string instructions, bool overwrite)
    {
        var safe = Normalize(name);
        if (safe.Length == 0) throw new ArgumentException("The skill needs a name with letters or digits.");
        var existing = Load(folder, null).FirstOrDefault(s => s.Name.Equals(safe, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !overwrite)
            throw new InvalidOperationException($"A skill \"{safe}\" already exists ({existing.FilePath}). Ask the user, then save with overwrite.");
        var file = existing?.FilePath ?? Path.Combine(folder, safe, FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var oneLine = description.Replace('\r', ' ').Replace('\n', ' ').Trim();
        File.WriteAllText(file, $"---\nname: {safe}\ndescription: {oneLine}\n---\n\n{instructions.Trim()}\n");
        return file;
    }

    [GeneratedRegex(@"^/([A-Za-z0-9_-]+)(?:\s+([\s\S]*))?$")]
    private static partial Regex CommandPattern();
}
