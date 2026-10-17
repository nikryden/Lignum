using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Serilog;

namespace Lignum.App.Assistant;

/// <summary>What a line of the AI panel is.</summary>
public enum ChatEntryKind
{
    User,
    Assistant,
    /// <summary>What an agent did: a tool call, or lines it read (so the user sees what was shared).</summary>
    Activity,
    Error,
}

/// <summary>One line of the AI panel.</summary>
public sealed partial class ChatEntry(ChatEntryKind kind, string text) : ObservableObject
{
    public ChatEntryKind Kind => kind;

    [ObservableProperty]
    private string _text = text;
}

/// <summary>
/// AI access (ticket 05): AI tools on this PC (GitHub Copilot, Claude Desktop, Claude Code, Cursor...) use
/// Lignum's tools through <c>Lignum.exe --mcp</c>, which relays to this running window over a named pipe. Off
/// until the user turns it on; while on, every call is listed in the panel and written to the diagnostics log.
/// The AI runs in the user's own tool, on their own subscription: Lignum holds no API key.
/// </summary>
public sealed partial class AssistantViewModel : ObservableObject
{
    private const int MaxEntries = 500;

    private readonly SettingsFile<AppSettings> _settings;
    private readonly AssistantTools _tools;
    private readonly SynchronizationContext? _uiContext;
    private readonly ILogger _log;
    private AiAccessServer? _server;
    private string? _pipeName;

    /// <summary>Whether the side panel is shown (View > AI access, Ctrl+Shift+A).</summary>
    [ObservableProperty]
    private bool _isPanelOpen;

    /// <summary>The chat's input box.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))]
    private bool _isBusy;

    private CliAgent? _agent;
    private CancellationTokenSource? _cancel;

    internal AssistantViewModel(SettingsFile<AppSettings> settings, AssistantTools tools, SynchronizationContext? uiContext,
                                ILogger log)
    {
        _settings = settings;
        _tools = tools;
        _uiContext = uiContext;
        _log = log;
        _tools.LinesRead += (file, lines) => Add(ChatEntryKind.Activity, $"Read {lines:N0} line{(lines == 1 ? "" : "s")} from {file}");
    }

    public ObservableCollection<ChatEntry> Entries { get; } = [];

    /// <summary>Off by default: no AI tool can use Lignum until the user turns this on.</summary>
    public bool IsEnabled
    {
        get => _settings.Value.Assistant.Enabled;
        set
        {
            if (value == IsEnabled) return;
            _settings.Value.Assistant.Enabled = value;
            _settings.Save();
            OnPropertyChanged();
            UpdateServer();
            Add(ChatEntryKind.Activity, value ? "AI access is on." : "AI access is off.");
        }
    }

    /// <summary>The pipe to serve on (set by the app once the window is up); null serves nothing.</summary>
    internal string? PipeName
    {
        get => _pipeName;
        set
        {
            _pipeName = value;
            UpdateServer();
        }
    }

    /// <summary>The arguments an AI tool passes to Lignum.exe (with --settings-dir when this run uses one).</summary>
    internal IReadOnlyList<string> McpArguments { get; set; } = ["--mcp"];

    /// <summary>The agents the chat can run (index = <see cref="AssistantSettings.Agent"/>).</summary>
    public IReadOnlyList<string> AgentNames { get; } = ["Claude Code", "GitHub Copilot CLI"];

    /// <summary>The agent the chat runs; changing it starts a new conversation.</summary>
    public int SelectedAgent
    {
        get => Math.Clamp(_settings.Value.Assistant.Agent, 0, AgentNames.Count - 1);
        set
        {
            if (value == SelectedAgent) return;
            _settings.Value.Assistant.Agent = value;
            _settings.Save();
            _agent = null;
            OnPropertyChanged();
            RefreshAgent();
        }
    }

    /// <summary>Whether the selected agent is installed on this PC.</summary>
    public bool IsAgentInstalled => CreateAgent(SelectedAgent).FindExecutable() is not null;

    /// <summary>The next step for the selected agent: install it, or sign in once.</summary>
    public string AgentStatus
    {
        get
        {
            var agent = CreateAgent(SelectedAgent);
            return agent.FindExecutable() is null
                ? $"{agent.Name} is not installed. {agent.InstallHint}"
                : $"{agent.Name} is installed. First time? Press Sign in once.";
        }
    }

    /// <summary>"Install": runs the vendor's official installer in a visible PowerShell window.</summary>
    [RelayCommand]
    private void InstallAgent()
    {
        var agent = CreateAgent(SelectedAgent);
        Add(ChatEntryKind.Activity, $"Installing {agent.Name} in a PowerShell window ({agent.InstallCommand}). Press Check again when it is done.");
        StartProcess(agent.InstallStart());
    }

    /// <summary>"Sign in": opens the agent in a terminal for its one-time sign-in.</summary>
    [RelayCommand]
    private void SignInAgent()
    {
        var agent = CreateAgent(SelectedAgent);
        if (agent.SignInStart() is not { } start)
        {
            RefreshAgent();
            return;
        }

        Add(ChatEntryKind.Activity, agent.SignInHint);
        StartProcess(start);
    }

    /// <summary>"Check again": looks for the agent again (after installing it).</summary>
    [RelayCommand]
    private void RefreshAgent()
    {
        OnPropertyChanged(nameof(IsAgentInstalled));
        OnPropertyChanged(nameof(AgentStatus));
    }

    /// <summary>Starts a window (installer, sign-in terminal); tests replace it.</summary>
    internal Action<System.Diagnostics.ProcessStartInfo> StartProcess { get; set; } = start =>
    {
        try { System.Diagnostics.Process.Start(start)?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { } // the user cancelled, or the program is gone
    };

    /// <summary>Creates the agent for a conversation (tests replace it).</summary>
    internal Func<int, string, CliAgent> AgentFactory { get; set; } = (index, folder) => index == 1
        ? new CopilotCliAgent { WorkFolder = folder, LignumExe = "", LignumArguments = [] }
        : new ClaudeCodeAgent { WorkFolder = folder, LignumExe = "", LignumArguments = [] };

    /// <summary>The Lignum.exe an AI tool starts.</summary>
    public string ExePath { get; } = Environment.ProcessPath ?? "Lignum.exe";

    /// <summary>The MCP setting to paste into an AI tool: "vscode", "claude-desktop" (also Cursor) or "claude-code".</summary>
    public string SetupSnippet(string tool)
    {
        var args = JsonSerializer.Serialize(McpArguments);
        var command = JsonSerializer.Serialize(ExePath);
        return tool switch
        {
            "vscode" => $$"""{ "servers": { "lignum": { "type": "stdio", "command": {{command}}, "args": {{args}} } } }""",
            "claude-code" => $"claude mcp add lignum -- \"{ExePath}\" {string.Join(' ', McpArguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}",
            _ => $$"""{ "mcpServers": { "lignum": { "command": {{command}}, "args": {{args}} } } }""",
        };
    }

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    [RelayCommand]
    private void TurnOn() => IsEnabled = true;

    [RelayCommand]
    private void TurnOff() => IsEnabled = false;

    [RelayCommand]
    private void Clear() => Entries.Clear();

    /// <summary>Starts a new conversation with the agent.</summary>
    [RelayCommand]
    private void NewConversation()
    {
        _cancel?.Cancel();
        _agent = null;
        Entries.Clear();
    }

    private bool CanSend() => IsEnabled && !IsBusy && !string.IsNullOrWhiteSpace(Input);

    /// <summary>Asks the selected agent; it uses Lignum's tools through MCP and its answer streams in.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        var question = Input.Trim();
        Input = "";
        Add(ChatEntryKind.User, question);
        var answer = new ChatEntry(ChatEntryKind.Assistant, "");
        Entries.Add(answer);
        _agent ??= CreateAgent(SelectedAgent);
        var agent = _agent;
        _cancel = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            await agent.RunTurnAsync(question, text => OnUi(() => answer.Text += text), _cancel.Token);
            OnUi(() =>
            {
                if (answer.Text.Trim().Length == 0) answer.Text = "(no answer)";
            });
        }
        catch (OperationCanceledException)
        {
            Add(ChatEntryKind.Activity, "Stopped.");
        }
        catch (Exception ex) when (ex is AgentException or System.ComponentModel.Win32Exception or IOException)
        {
            if (answer.Text.Length == 0) Entries.Remove(answer);
            Add(ChatEntryKind.Error, ex.Message);
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

    private CliAgent CreateAgent(int index)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lignum",
            "agents", Guid.NewGuid().ToString("N"));
        var made = AgentFactory(index, folder);
        return made switch
        {
            ClaudeCodeAgent when made.LignumExe.Length == 0 => new ClaudeCodeAgent
                { WorkFolder = folder, LignumExe = ExePath, LignumArguments = McpArguments, Executable = made.Executable },
            CopilotCliAgent when made.LignumExe.Length == 0 => new CopilotCliAgent
                { WorkFolder = folder, LignumExe = ExePath, LignumArguments = McpArguments, Executable = made.Executable },
            _ => made,
        };
    }

    private void OnUi(Action action)
    {
        if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }

    /// <summary>Runs one tool call from an AI agent on the UI thread, listing it in the panel.</summary>
    internal Task<ToolOutcome> HandleToolCallAsync(ToolCall call, CancellationToken cancellationToken)
    {
        if (_uiContext is null) return RunToolAsync(call, cancellationToken);
        var done = new TaskCompletionSource<ToolOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(async _ =>
        {
            try
            {
                done.SetResult(await RunToolAsync(call, cancellationToken));
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }, null);
        return done.Task;
    }

    private async Task<ToolOutcome> RunToolAsync(ToolCall call, CancellationToken cancellationToken)
    {
        if (!IsEnabled) return new ToolOutcome(AiPipeClient.NotAvailableMessage, IsError: true);
        Add(ChatEntryKind.Activity, Describe(call));
        _log.Information("AI tool call {Tool} {Input}", call.Name, JsonSerializer.Serialize(call.Input));
        var outcome = await _tools.RunAsync(call, cancellationToken);
        if (outcome.IsError) Add(ChatEntryKind.Error, $"  failed: {outcome.Content}");
        return outcome;
    }

    private void UpdateServer()
    {
        var serve = IsEnabled && _pipeName is not null;
        if (serve && _server is null) _server = new AiAccessServer(_pipeName!, HandleToolCallAsync);
        else if (!serve && _server is not null)
        {
            _server.Dispose();
            _server = null;
        }
    }

    private void Add(ChatEntryKind kind, string text)
    {
        Entries.Add(new ChatEntry(kind, text));
        if (Entries.Count > MaxEntries) Entries.RemoveAt(0);
    }

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
