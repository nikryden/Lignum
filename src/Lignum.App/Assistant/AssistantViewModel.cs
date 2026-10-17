using System.Collections.ObjectModel;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Serilog;

namespace Lignum.App.Assistant;

/// <summary>What a line of the conversation is.</summary>
public enum ChatEntryKind
{
    User,
    Assistant,
    /// <summary>What the assistant did: a tool call, or lines it read (so the user sees what was sent).</summary>
    Activity,
    Error,
}

/// <summary>One line of the conversation; assistant text grows while it streams.</summary>
public sealed partial class ChatEntry(ChatEntryKind kind, string text) : ObservableObject
{
    public ChatEntryKind Kind => kind;

    [ObservableProperty]
    private string _text = text;
}

/// <summary>
/// The built-in assistant (ticket 05): a conversation with Claude, which can look at the open logs and add
/// filter rules, highlight rules and bookmarks through <see cref="AssistantTools"/>. Off until the user turns
/// it on; then the lines its tools read are sent to Anthropic's API with the user's own API key. Every tool
/// call is shown in the conversation and written to the diagnostics log.
/// </summary>
public sealed partial class AssistantViewModel : ObservableObject
{
    public const string DefaultModel = "claude-opus-5-5";
    private const int MaxToolRounds = 20;

    private const string SystemPrompt = """
        You are the assistant built into Lignum, a read-only Windows viewer for very large log files. The user is
        looking at log files in Lignum; use the tools to look at them instead of guessing, and never invent log content.
        Line numbers are 1-based, as Lignum shows them.
        Read only what you need: search with a precise pattern first, then read_lines around the lines that matter.
        When you write a regular expression, use .NET syntax and check it with search before you suggest or apply it.
        You may add filter rules, highlight rules and bookmarks, and move to a line, when that helps; say what you changed.
        Keep answers short and concrete, and quote line numbers.
        """;

    private readonly SettingsFile<AppSettings> _settings;
    private readonly AssistantTools _tools;
    private readonly Func<string?, IClaudeChat> _chatFactory;
    private readonly ILogger _log;
    private readonly List<BetaMessageParam> _history = [];
    private CancellationTokenSource? _cancel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))]
    private bool _isBusy;

    internal AssistantViewModel(SettingsFile<AppSettings> settings, AssistantTools tools,
                                Func<string?, IClaudeChat> chatFactory, ILogger log)
    {
        _settings = settings;
        _tools = tools;
        _chatFactory = chatFactory;
        _log = log;
        _tools.LinesRead += (file, lines) =>
            Entries.Add(new ChatEntry(ChatEntryKind.Activity, $"Claude read {lines:N0} line{(lines == 1 ? "" : "s")} from {file}"));
    }

    public ObservableCollection<ChatEntry> Entries { get; } = [];

    /// <summary>Raised after each tool call (tests).</summary>
    internal event Action<ToolCall, ToolOutcome>? ToolRan;

    /// <summary>Off by default: nothing is sent anywhere until the user turns the assistant on.</summary>
    public bool IsEnabled
    {
        get => _settings.Value.Assistant.Enabled;
        set
        {
            if (value == IsEnabled) return;
            _settings.Value.Assistant.Enabled = value;
            _settings.Save();
            OnPropertyChanged();
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>The Claude model, e.g. claude-opus-5-5.</summary>
    public string Model
    {
        get => _settings.Value.Assistant.Model is { Length: > 0 } model ? model : DefaultModel;
        set
        {
            _settings.Value.Assistant.Model = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>A key is saved, or ANTHROPIC_API_KEY is set.</summary>
    public bool HasApiKey =>
        _settings.Value.Assistant.ProtectedApiKey is not null
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

    /// <summary>Saves the API key encrypted for this Windows user (DPAPI); null or empty removes it.</summary>
    public void SetApiKey(string? key)
    {
        _settings.Value.Assistant.ProtectedApiKey = string.IsNullOrWhiteSpace(key)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null,
                DataProtectionScope.CurrentUser));
        _settings.Save();
        OnPropertyChanged(nameof(HasApiKey));
    }

    /// <summary>The saved key, decrypted; null to let the SDK use ANTHROPIC_API_KEY.</summary>
    internal string? ApiKey()
    {
        if (_settings.Value.Assistant.ProtectedApiKey is not { } stored) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null,
                DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null; // saved by another Windows user or damaged: fall back to the environment
        }
    }

    private bool CanSend() => IsEnabled && !IsBusy && !string.IsNullOrWhiteSpace(Input);

    /// <summary>Sends <see cref="Input"/> and runs Claude's tool calls until it answers; completes when done.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        var question = Input.Trim();
        Input = "";
        Entries.Add(new ChatEntry(ChatEntryKind.User, question));
        var historyBefore = _history.Count;
        _history.Add(new BetaMessageParam { Role = Role.User, Content = question });
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        IsBusy = true;
        try
        {
            var chat = _chatFactory(ApiKey());
            for (var round = 0; round < MaxToolRounds; round++)
            {
                var entry = new ChatEntry(ChatEntryKind.Assistant, "");
                Entries.Add(entry);
                var reply = await chat.SendAsync(BuildRequest(), text => entry.Text += text, token);
                if (entry.Text.Length == 0) entry.Text = reply.Text;
                if (entry.Text.Length == 0) Entries.Remove(entry);
                _history.Add(new BetaMessageParam { Role = Role.Assistant, Content = reply.Echo });

                if (reply.StopReason == "refusal")
                {
                    AddError("Claude declined to answer this.");
                    break;
                }

                if (reply.StopReason == "max_tokens")
                {
                    AddError("The answer was cut off (too long).");
                    if (reply.ToolCalls.Count > 0) RollBack(historyBefore); // half-written tool calls can't be answered
                    break;
                }

                if (reply.StopReason != "tool_use" || reply.ToolCalls.Count == 0) break;

                var results = new List<BetaContentBlockParam>();
                foreach (var call in reply.ToolCalls)
                {
                    Entries.Add(new ChatEntry(ChatEntryKind.Activity, Describe(call)));
                    _log.Information("Assistant tool call {Tool} {Input}", call.Name, JsonSerializer.Serialize(call.Input));
                    var outcome = await _tools.RunAsync(call, token);
                    ToolRan?.Invoke(call, outcome);
                    if (outcome.IsError) Entries.Add(new ChatEntry(ChatEntryKind.Activity, $"  failed: {outcome.Content}"));
                    results.Add(new BetaToolResultBlockParam
                    {
                        ToolUseID = call.Id,
                        Content = outcome.Content,
                        IsError = outcome.IsError,
                    });
                }

                _history.Add(new BetaMessageParam { Role = Role.User, Content = results }); // all results in one message
            }
        }
        catch (OperationCanceledException)
        {
            RollBack(historyBefore);
            Entries.Add(new ChatEntry(ChatEntryKind.Activity, "Stopped. That question is not kept in the conversation."));
        }
        catch (AnthropicRateLimitException)
        {
            RollBack(historyBefore);
            AddError("Claude is rate limited right now; try again in a minute.");
        }
        catch (AnthropicApiException ex)
        {
            RollBack(historyBefore);
            AddError($"Claude API error: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            RollBack(historyBefore);
            AddError($"Could not reach Claude: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cancel.Dispose();
            _cancel = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop() => _cancel?.Cancel();

    /// <summary>Starts a new conversation.</summary>
    [RelayCommand]
    private void Clear()
    {
        _cancel?.Cancel();
        _history.Clear();
        Entries.Clear();
    }

    private MessageCreateParams BuildRequest() => new()
    {
        Model = Model,
        MaxTokens = 32000,
        System = SystemPrompt,
        Tools = [.. AssistantTools.Definitions],
        Messages = [.. _history],
        CacheControl = new BetaCacheControlEphemeral(), // the system prompt, tools and history are reused every round
    };

    /// <summary>Drops a failed or stopped exchange, so the history stays a valid, append-only conversation.</summary>
    private void RollBack(int count)
    {
        if (_history.Count > count) _history.RemoveRange(count, _history.Count - count);
    }

    private void AddError(string message) => Entries.Add(new ChatEntry(ChatEntryKind.Error, message));

    private static string Describe(ToolCall call) => call.Name switch
    {
        "search" => $"Searching for \"{Arg(call, "pattern")}\"",
        "read_lines" => $"Reading lines from {Arg(call, "first_line")}",
        "add_filter_rule" => $"Adding {Arg(call, "kind")} filter \"{Arg(call, "text")}\"",
        "add_highlight_rule" => $"Adding highlight rule \"{Arg(call, "pattern")}\"",
        "add_bookmark" => $"Bookmarking line {Arg(call, "line")}",
        "go_to_line" => $"Going to line {Arg(call, "line")}",
        "list_tabs" => "Listing the open files",
        _ => call.Name,
    };

    private static string Arg(ToolCall call, string name) =>
        call.Input.TryGetValue(name, out var value) ? value.ToString() : "?";
}
