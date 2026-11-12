using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lignum.App.Assistant;

/// <summary>One saved line of a chat.</summary>
public sealed record ChatLine(ChatEntryKind Kind, string Text);

/// <summary>
/// A saved AI chat (ticket 32): its lines and what is needed to continue it with the agent. Like Claude Code's
/// <c>--resume</c>, the agent CLI keeps the conversation itself: Lignum stores the agent's session id (Claude) or its
/// working folder (Copilot, Cursor and Codex continue the last session in that folder).
/// </summary>
public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "";

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset Updated { get; set; } = DateTimeOffset.Now;

    /// <summary>The agent's index in the agent list.</summary>
    public int Agent { get; set; }

    /// <summary>The agent's own session id, for agents that resume by id (Claude Code).</summary>
    public string? AgentSessionId { get; set; }

    /// <summary>The agent's working folder; agents that continue "the last session here" resume from it.</summary>
    public string? WorkFolder { get; set; }

    /// <summary>Whether the agent has answered at least once, so the next turn is a follow-up.</summary>
    public bool HasStarted { get; set; }

    public List<ChatLine> Lines { get; set; } = [];
}

/// <summary>A chat in the history list.</summary>
public sealed record ChatSummary(string Id, string Title, DateTimeOffset Updated)
{
    public string When => Updated.LocalDateTime.ToString("g");
}

/// <summary>Saves chats as <c>chats/&lt;id&gt;.json</c> in the settings folder (ticket 32).</summary>
public sealed class ChatHistoryStore(string folder)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Folder => folder;

    /// <summary>Every saved chat, newest first. Unreadable files are skipped.</summary>
    public IReadOnlyList<ChatSummary> List()
    {
        if (!Directory.Exists(folder)) return [];
        var chats = new List<ChatSummary>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            if (Read(file) is { } chat) chats.Add(new ChatSummary(chat.Id, chat.Title, chat.Updated));
        }

        return [.. chats.OrderByDescending(c => c.Updated)];
    }

    public ChatSession? Load(string id) => IsSafeId(id) ? Read(PathOf(id)) : null;

    /// <summary>Writes the chat to a temporary file first, then replaces the old one, so a crash never leaves half a file.</summary>
    public void Save(ChatSession chat)
    {
        if (!IsSafeId(chat.Id)) throw new ArgumentException("Bad chat id.", nameof(chat));
        Directory.CreateDirectory(folder);
        var path = PathOf(chat.Id);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(chat, Json));
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string id)
    {
        if (IsSafeId(id)) File.Delete(PathOf(id));
    }

    public void DeleteAll()
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json")) File.Delete(file);
    }

    /// <summary>
    /// The earlier turns as text, for an agent that cannot resume the chat itself: the last <paramref name="maxTurns"/>
    /// questions and answers, at most <paramref name="maxChars"/> characters (older text is dropped first).
    /// </summary>
    public static string ContextPreamble(IEnumerable<ChatLine> lines, int maxTurns = 20, int maxChars = 32 * 1024)
    {
        var turns = lines.Where(l => l.Kind is ChatEntryKind.User or ChatEntryKind.Assistant && l.Text.Trim().Length > 0)
            .Select(l => (l.Kind == ChatEntryKind.User ? "User: " : "Assistant: ") + l.Text.Trim())
            .TakeLast(maxTurns * 2).ToList();
        if (turns.Count == 0) return "";
        var kept = new List<string>();
        var size = 0;
        for (var i = turns.Count - 1; i >= 0 && size + turns[i].Length <= maxChars; i--)
        {
            kept.Insert(0, turns[i]);
            size += turns[i].Length + 2;
        }

        return kept.Count == 0 ? "" : "Conversation so far:\n\n" + string.Join("\n\n", kept) + "\n\nNew question: ";
    }

    private string PathOf(string id) => Path.Combine(folder, id + ".json");

    private static bool IsSafeId(string id) => id.Length > 0 && id.All(char.IsAsciiLetterOrDigit);

    private static ChatSession? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<ChatSession>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
