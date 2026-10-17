using System.Text;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>
/// "Copy for AI" (ticket 05): log lines with their file name and line numbers, wrapped in a short prompt, ready to
/// paste into any AI chat (Claude.ai, ChatGPT, Copilot Chat...). Reads by position, never the whole file.
/// </summary>
public static class AiPrompt
{
    public const int MaxLines = 500;
    private const int MaxLineChars = 2000;

    /// <summary>Builds the text for <paramref name="lines"/> (0-based file lines, ascending). Reads the file: call off the UI thread.</summary>
    public static string Build(string fileName, LogDocument document, IReadOnlyList<long> lines)
    {
        var shown = Math.Min(lines.Count, MaxLines);
        var text = new StringBuilder();
        text.AppendLine($"Here are lines from the log file \"{fileName}\" (copied from Lignum). Each line starts with its line number.");
        text.AppendLine();
        text.AppendLine("```text");
        for (var i = 0; i < shown; i++)
        {
            var prefix = document.ReadLinePrefixes(lines[i], 1, MaxLineChars);
            var line = prefix.Length == 0 ? "" : prefix[0].Text + (prefix[0].IsTruncated ? " …" : "");
            text.AppendLine($"{lines[i] + 1}: {line}");
        }

        text.AppendLine("```");
        if (shown < lines.Count) text.AppendLine($"(Only the first {MaxLines} of {lines.Count:N0} selected lines are included.)");
        text.AppendLine();
        text.Append("My question: ");
        return text.ToString();
    }
}
