using System.Text;
using System.Text.Json;
using Lignum.App.Assistant.Permissions;

namespace Lignum.App.Assistant;

// File tools (ticket 18, ADR 0002): the AI reads folders and files anywhere and writes files, each call allowed by the
// permission store or by the user. Log files open in a tab and Lignum's settings folder are never written.
public sealed partial class AssistantTools
{
    internal const int MaxListEntries = 500;
    internal const int MaxReadFileLines = 500;
    internal const long MaxReadFileBytes = 50L * 1024 * 1024; // bigger: open it in a tab and use read_lines
    internal const long MaxEditFileBytes = 5L * 1024 * 1024;
    internal const int MaxWriteChars = 2_000_000;
    private const int MaxPreviewLines = 200;

    private Task<bool> AllowedAsync(PermissionRequest request, CancellationToken cancellationToken) =>
        main.Assistant.RequestPermissionAsync(request, cancellationToken);

    private static ToolOutcome Denied(string what) =>
        new($"The user did not allow {what}. Do not retry it; ask the user if it is needed.", IsError: true);

    private static string FullPath(IReadOnlyDictionary<string, JsonElement> input, string name = "path")
    {
        try
        {
            return Path.GetFullPath(String(input, name));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ToolInputException($"\"{name}\" is not a valid path: {ex.Message}");
        }
    }

    private static string OptionalText(IReadOnlyDictionary<string, JsonElement> input, string name) =>
        !input.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null ? ""
        : value.ValueKind == JsonValueKind.String ? value.GetString() ?? ""
        : throw new ToolInputException($"\"{name}\" must be a string.");

    /// <summary>Folders first, then files, with sizes; at most <see cref="MaxListEntries"/>.</summary>
    private async Task<ToolOutcome> ListDirAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken)
    {
        var path = FullPath(input);
        if (!Directory.Exists(path)) throw new ToolInputException($"No folder {path}.");
        if (!await AllowedAsync(new PermissionRequest(AiAccess.Read, path, IsFolder: true), cancellationToken))
            return Denied($"reading {path}");

        return await Task.Run(() =>
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
            var folder = new DirectoryInfo(path);
            var entries = folder.EnumerateDirectories("*", options).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => $"{d.Name}\\")
                .Concat(folder.EnumerateFiles("*", options).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(f => $"{f.Name}  ({f.Length:N0} bytes)"))
                .Take(MaxListEntries + 1).ToList();
            var text = new StringBuilder($"{path}:\n");
            foreach (var entry in entries.Take(MaxListEntries)) text.AppendLine(entry);
            if (entries.Count > MaxListEntries) text.AppendLine($"… more than {MaxListEntries} entries; list a subfolder.");
            if (entries.Count == 0) text.AppendLine("(empty)");
            return new ToolOutcome(text.ToString());
        }, cancellationToken);
    }

    /// <summary>Lines of a text file with 1-based numbers, without loading the whole file.</summary>
    private async Task<ToolOutcome> ReadFileAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken)
    {
        var path = FullPath(input);
        if (!File.Exists(path)) throw new ToolInputException($"No file {path}.");
        var size = new FileInfo(path).Length;
        if (size > MaxReadFileBytes)
            throw new ToolInputException($"{path} is {size:N0} bytes; open it in a tab with open_file and use search and read_lines.");
        var first = Math.Max(1, Int(input, "first_line", 1));
        var count = Math.Clamp(Int(input, "count", 200), 1, MaxReadFileLines);
        if (!await AllowedAsync(new PermissionRequest(AiAccess.Read, path), cancellationToken)) return Denied($"reading {path}");

        return await Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            var text = new StringBuilder();
            var number = 0;
            var shown = 0;
            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++number < first) continue;
                if (shown == count)
                {
                    text.AppendLine($"… more lines follow; read on from line {number}.");
                    break;
                }

                text.Append(number).Append(": ").AppendLine(line.Length > MaxLineChars ? line[..MaxLineChars] + " …" : line);
                shown++;
            }

            if (shown == 0) text.AppendLine(number == 0 ? "(empty file)" : $"The file has {number:N0} lines.");
            return new ToolOutcome(text.ToString());
        }, cancellationToken);
    }

    /// <summary>Creates or replaces a text file (UTF-8, or the existing file's encoding).</summary>
    private async Task<ToolOutcome> WriteFileAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken)
    {
        var path = FullPath(input);
        var content = OptionalText(input, "content");
        if (content.Length > MaxWriteChars) throw new ToolInputException($"The content is over {MaxWriteChars:N0} characters.");
        RefuseProtected(path);
        if (Directory.Exists(path)) throw new ToolInputException($"{path} is a folder.");
        var exists = File.Exists(path);
        var detail = (exists ? $"Replaces the file ({new FileInfo(path).Length:N0} bytes) with:\n" : "New file:\n") + Preview(content, "");
        if (!await AllowedAsync(new PermissionRequest(AiAccess.Write, path, Detail: detail), cancellationToken)) return Denied($"writing {path}");

        await Task.Run(() =>
        {
            var encoding = exists ? EncodingOf(path) : new UTF8Encoding(false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, encoding);
        }, cancellationToken);
        return new ToolOutcome($"{(exists ? "Replaced" : "Created")} {path} ({content.Length:N0} characters).");
    }

    /// <summary>Replaces exact text in a file: once (it must occur once) or everywhere with replace_all.</summary>
    private async Task<ToolOutcome> EditFileAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken)
    {
        var path = FullPath(input);
        var oldText = String(input, "old_text");
        var newText = OptionalText(input, "new_text");
        var all = Bool(input, "replace_all");
        RefuseProtected(path);
        if (!File.Exists(path)) throw new ToolInputException($"No file {path}; create it with write_file.");
        if (new FileInfo(path).Length > MaxEditFileBytes) throw new ToolInputException($"{path} is over {MaxEditFileBytes:N0} bytes.");

        var encoding = EncodingOf(path);
        var text = await File.ReadAllTextAsync(path, encoding, cancellationToken);
        var count = Occurrences(text, oldText);
        if (count == 0) throw new ToolInputException("old_text was not found; read the file again and copy the text exactly.");
        if (count > 1 && !all)
            throw new ToolInputException($"old_text occurs {count} times; add surrounding text to make it unique, or set replace_all.");
        var detail = $"{(all && count > 1 ? $"{count} replacements:\n" : "")}{Preview(oldText, "- ")}{Preview(newText, "+ ")}";
        if (!await AllowedAsync(new PermissionRequest(AiAccess.Write, path, Detail: detail), cancellationToken)) return Denied($"editing {path}");

        var updated = all ? text.Replace(oldText, newText, StringComparison.Ordinal) : ReplaceFirst(text, oldText, newText);
        await File.WriteAllTextAsync(path, updated, encoding, cancellationToken);
        return new ToolOutcome($"Edited {path}: {(all ? count : 1)} replacement{((all ? count : 1) == 1 ? "" : "s")}.");
    }

    /// <summary>Log files open in a tab and Lignum's settings folder are never written (hard constraint, ADR 0002).</summary>
    private void RefuseProtected(string path)
    {
        var target = CanonicalPath.Of(path);
        foreach (var tab in main.Tabs)
        {
            if (string.Equals(target, CanonicalPath.Of(tab.Path), StringComparison.OrdinalIgnoreCase)
                || string.Equals(target, CanonicalPath.Of(tab.SourcePath), StringComparison.OrdinalIgnoreCase))
                throw new ToolInputException($"{path} is open in Lignum as a log, and Lignum never writes to a log file.");
        }

        if (CanonicalPath.IsUnder(target, CanonicalPath.Of(main.SettingsFolder)))
            throw new ToolInputException("Lignum's settings folder cannot be written by the AI.");
    }

    private static Encoding EncodingOf(string path)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        reader.Peek();
        return reader.CurrentEncoding;
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string ReplaceFirst(string text, string oldText, string newText)
    {
        var i = text.IndexOf(oldText, StringComparison.Ordinal);
        return string.Concat(text.AsSpan(0, i), newText, text.AsSpan(i + oldText.Length));
    }

    /// <summary>The first lines of <paramref name="text"/>, each with <paramref name="prefix"/>, for the approval dialog.</summary>
    private static string Preview(string text, string prefix)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var preview = new StringBuilder();
        foreach (var line in lines.Take(MaxPreviewLines)) preview.Append(prefix).AppendLine(line);
        if (lines.Length > MaxPreviewLines) preview.AppendLine($"… {lines.Length - MaxPreviewLines:N0} more lines");
        return preview.ToString();
    }
}
