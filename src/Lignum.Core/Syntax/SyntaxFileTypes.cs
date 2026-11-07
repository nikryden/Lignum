namespace Lignum.Core.Syntax;

/// <summary>Which <see cref="SyntaxScheme"/> a file uses, by its name or extension (ticket 25).</summary>
public static class SyntaxFileTypes
{
    /// <summary>Log and text types: never syntax colored, they keep the whole-line highlight rules.</summary>
    public static readonly IReadOnlySet<string> Excluded =
        new HashSet<string>([".txt", ".log", ".lm"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses an extension list as typed: separated by <c>;</c>, <c>,</c> or spaces; <c>cs</c>, <c>.cs</c> and
    /// <c>*.cs</c> all mean <c>.cs</c>; a word ending in <c>file</c> (<c>Dockerfile</c>, <c>Makefile</c>) is a
    /// whole file name. Lowercased, without duplicates, and without <see cref="Excluded"/> types.
    /// </summary>
    public static List<string> ParseExtensions(string? text)
    {
        var result = new List<string>();
        foreach (var raw in (text ?? "").Split([';', ',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = raw.Trim();
            if (entry.StartsWith("*.", StringComparison.Ordinal)) entry = entry[1..];
            else if (!entry.StartsWith('.') && !IsFileName(entry)) entry = "." + entry;
            entry = entry.ToLowerInvariant();
            if (entry.Length > 1 && !Excluded.Contains(entry) && !result.Contains(entry)) result.Add(entry);
        }

        return result;
    }

    /// <summary>The extensions as the editor shows them: <c>.cs; .csx</c>.</summary>
    public static string FormatExtensions(IEnumerable<string> extensions) => string.Join("; ", extensions);

    /// <summary>
    /// The scheme for <paramref name="path"/>: the first one listing its file name, else the first one listing its
    /// extension. Null for <see cref="Excluded"/> types and types without a scheme.
    /// </summary>
    public static SyntaxScheme? Find(IReadOnlyList<SyntaxScheme> schemes, string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var name = Path.GetFileName(path).ToLowerInvariant();
        var extension = Path.GetExtension(name);
        if (Excluded.Contains(extension)) return null;
        return schemes.FirstOrDefault(s => s.Extensions.Contains(name))
               ?? (extension.Length > 1 ? schemes.FirstOrDefault(s => s.Extensions.Contains(extension)) : null);
    }

    // Dockerfile, Makefile, Jenkinsfile, Gemfile, Vagrantfile… and a few other extensionless names.
    private static bool IsFileName(string entry) =>
        (entry.Length > 4 && entry.EndsWith("file", StringComparison.OrdinalIgnoreCase))
        || entry.Equals("gnumakefile", StringComparison.OrdinalIgnoreCase);
}
