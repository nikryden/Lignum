using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lignum.App.Settings;

/// <summary>
/// The bookmarks of one log file: <c>bookmarks/&lt;hash&gt;.json</c> in the <see cref="SettingsStore"/>, where
/// the hash is of the file's full path (see <see cref="RelativePathFor"/>). Example:
/// <code>{ "path": "C:\\logs\\app.log", "bookmarks": [ { "line": 122 }, { "line": 4096 } ] }</code>
/// Each bookmark is an object, not a bare number, so fields can be added later (a planned <c>note</c>) without a
/// migration: old files simply lack them, and fields this build does not know are kept and written back.
/// </summary>
public sealed class BookmarksFile
{
    public const string Folder = "bookmarks";

    /// <summary>The log file's full path, for people browsing the folder. Not used to find the file.</summary>
    public string? Path { get; set; }

    public List<BookmarkEntry> Bookmarks { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>
    /// <c>bookmarks/&lt;sha256 of the upper-cased full path&gt;.json</c>. Upper-cased because Windows paths ignore
    /// case, so every spelling of a path shares its bookmarks (tabs compare paths the same way).
    /// </summary>
    public static string RelativePathFor(string fullPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()));
        return System.IO.Path.Combine(Folder, Convert.ToHexStringLower(hash) + ".json");
    }
}

/// <summary>One bookmarked line. Add optional fields here; old files stay valid.</summary>
public sealed class BookmarkEntry
{
    /// <summary>0-based file line.</summary>
    public long Line { get; set; }

    /// <summary>The user's note on this line (ticket 06); null (and not written) when there is none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }

    /// <summary>Fields written by a newer build (e.g. a note), kept so a save here does not drop them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
