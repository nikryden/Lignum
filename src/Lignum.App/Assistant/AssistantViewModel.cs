using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Assistant.Permissions;
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

    private readonly ViewModels.DetailPaneLayoutViewModel _layout;

    /// <summary>The chat's input box.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))]
    private bool _isBusy;

    /// <summary>The skills matching the "/name" being typed at the start of <see cref="Input"/>.</summary>
    public ObservableCollection<Skill> SkillSuggestions { get; } = [];

    [ObservableProperty]
    private bool _isSkillListOpen;

    [ObservableProperty]
    private Skill? _selectedSkillSuggestion;

    /// <summary>The skills last read from disk; read again each time a "/" starts the input.</summary>
    private IReadOnlyList<Skill> _skills = [];

    partial void OnInputChanged(string value)
    {
        // Suggest while the input is a "/" followed only by name characters.
        var typing = value.StartsWith('/') && !value.Skip(1).Any(char.IsWhiteSpace);
        if (!typing)
        {
            IsSkillListOpen = false;
            return;
        }

        if (!IsSkillListOpen || value == "/") _skills = LoadSlashEntries();
        var prefix = value[1..];
        SkillSuggestions.Clear();
        foreach (var skill in _skills.Where(s => s.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(s => !s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            SkillSuggestions.Add(skill);
        SelectedSkillSuggestion = SkillSuggestions.FirstOrDefault();
        // With nothing to suggest, say where skills were looked for instead of showing nothing.
        SkillListHint = SkillSuggestions.Count == 0 ? $"No command or skill matches /{prefix}."
            : _skills.Any(s => s.Scope is SkillScope.Global or SkillScope.Local) ? null
            : "No skills of your own for this log yet. Global skills: " + _tools.GlobalSkillsFolder
              + (_tools.LocalSkillsFolder is { } local ? $"\nLocal skills (this log's folder only): {local}" : "")
              + "\nAsk the AI to make one, or put a SKILL.md in a subfolder there.";
        IsSkillListOpen = true;
    }

    /// <summary>
    /// Commands Lignum runs itself when typed as "/name" (nothing goes to the agent). Their names win over any skill.
    /// </summary>
    public static IReadOnlyList<Skill> ChatCommands { get; } =
    [
        new("clear", "Start a new conversation (the agent forgets this one)", SkillScope.Command, ""),
        new("new", "Same as /clear", SkillScope.Command, ""),
        new("model", "Show or set the model, e.g. /model opus (empty: the agent's default)", SkillScope.Command, ""),
        new("effort", "Show or set the reasoning level, e.g. /effort high", SkillScope.Command, ""),
        new("permissions", "Show and revoke what the AI may do without asking", SkillScope.Command, ""),
        new("help", "List the commands and skills", SkillScope.Command, ""),
    ];

    /// <summary>Raised by /permissions: the window opens View → AI permissions….</summary>
    public event EventHandler? PermissionsRequested;

    /// <summary>
    /// Everything "/" offers: chat commands first, then Lignum's skills (one named like a command is hidden), then the
    /// agent's own skills whose names are not taken.
    /// </summary>
    private IReadOnlyList<Skill> LoadSlashEntries()
    {
        List<Skill> entries = [.. ChatCommands, .. _tools.LoadSkills().Where(s => !IsChatCommand(s.Name))];
        var taken = entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        entries.AddRange((_agent ?? CreateAgent(SelectedAgent)).LoadOwnSkills().Where(s => !taken.Contains(s.Name)));
        return entries;
    }

    private static bool IsChatCommand(string name) =>
        ChatCommands.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs a chat command; the input is cleared and the command echoed as an activity line.</summary>
    private void RunChatCommand(string name, string rest)
    {
        Input = "";
        IsSkillListOpen = false;
        switch (name)
        {
            case "clear" or "new":
                NewConversation();
                break;
            case "model":
                if (rest.Length > 0) AgentModel = rest == "default" ? "" : rest;
                Add(ChatEntryKind.Activity, $"Model: {(AgentModel.Length > 0 ? AgentModel : "the agent's default")}");
                break;
            case "effort" when EffortChoices.Count <= 1:
                Add(ChatEntryKind.Error, $"{AgentNames[SelectedAgent]} has no reasoning level setting.");
                break;
            case "effort" when !SupportsEffort:
                Add(ChatEntryKind.Error, $"With model \"{(AgentModel.Length > 0 ? AgentModel : "default")}\" {AgentNames[SelectedAgent]} " +
                    "picks the model itself and takes no reasoning level. Choose a model first, e.g. /model <name>.");
                break;
            case "effort" when rest.Length > 0 && !EffortChoices.Contains(rest.ToLowerInvariant()) && rest != "default":
                Add(ChatEntryKind.Error,
                    $"Reasoning levels for {AgentNames[SelectedAgent]}: {string.Join(", ", EffortChoices.Skip(1))} (or default).");
                break;
            case "effort":
                if (rest.Length > 0) AgentEffort = rest == "default" ? "" : rest.ToLowerInvariant();
                Add(ChatEntryKind.Activity, $"Reasoning level: {(AgentEffort.Length > 0 ? AgentEffort : "the agent's default")}");
                break;
            case "permissions":
                PermissionsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "help":
                var lines = LoadSlashEntries().Select(s => $"/{s.Name} ({s.ScopeText}): {s.Description}");
                Add(ChatEntryKind.Activity, "Commands and skills:\n" + string.Join("\n", lines));
                break;
        }
    }

    /// <summary>Shown in the skill list when nothing matches the "/name" being typed; null when there are suggestions.</summary>
    [ObservableProperty]
    private string? _skillListHint;

    /// <summary>Puts "/name " in the input for the chosen suggestion (Tab, Enter or a click).</summary>
    public void AcceptSkillSuggestion(Skill? skill = null)
    {
        if ((skill ?? SelectedSkillSuggestion) is not { } chosen) return;
        Input = $"/{chosen.Name} ";
        IsSkillListOpen = false;
    }

    /// <summary>Moves the highlighted suggestion by <paramref name="delta"/>, wrapping.</summary>
    public void MoveSkillSuggestion(int delta)
    {
        if (SkillSuggestions.Count == 0) return;
        var index = SelectedSkillSuggestion is { } current ? SkillSuggestions.IndexOf(current) : -1;
        SelectedSkillSuggestion = SkillSuggestions[((index + delta) % SkillSuggestions.Count + SkillSuggestions.Count) % SkillSuggestions.Count];
    }

    [RelayCommand]
    private void OpenGlobalSkillsFolder() => OpenFolder(_tools.GlobalSkillsFolder);

    [RelayCommand]
    private void OpenLocalSkillsFolder()
    {
        if (_tools.LocalSkillsFolder is { } folder) OpenFolder(folder);
        else Add(ChatEntryKind.Error, "Open a log first: local skills are kept next to it.");
    }

    private void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            StartProcess(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Add(ChatEntryKind.Error, $"Could not open {folder}: {ex.Message}");
        }
    }

    private CliAgent? _agent;
    private CancellationTokenSource? _cancel;

    internal AssistantViewModel(SettingsFile<AppSettings> settings, AssistantTools tools, SynchronizationContext? uiContext,
                                ILogger log, ViewModels.DetailPaneLayoutViewModel layout)
    {
        _layout = layout;
        _layout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ViewModels.DetailPaneLayoutViewModel.IsOpen)) return;
            OnPropertyChanged(nameof(IsPanelOpen));
            OnPanelShown();
        };
        _settings = settings;
        _tools = tools;
        _uiContext = uiContext;
        _log = log;
        _tools.LinesRead += (file, lines) => Add(ChatEntryKind.Activity, $"Read {lines:N0} line{(lines == 1 ? "" : "s")} from {file}");
        Permissions = new PermissionStore(settings);
    }

    /// <summary>What the AI may do beyond the open logs (ticket 17, ADR 0002); revoked under View → AI permissions….</summary>
    public PermissionStore Permissions { get; }

    /// <summary>Asks the user about a permission; null (tests, no window) denies everything not already granted.</summary>
    public IPermissionApprover? Approver { get; set; }

    /// <summary>
    /// The one gate for powerful tools: true when a grant covers <paramref name="request"/> or the user allows it now.
    /// </summary>
    internal Task<bool> RequestPermissionAsync(PermissionRequest request, CancellationToken cancellationToken) =>
        Approver is { } approver
            ? Permissions.RequestAsync(request, approver, cancellationToken)
            : Task.FromResult(Permissions.IsAllowed(request));

    public ObservableCollection<ChatEntry> Entries { get; } = [];

    /// <summary>Lignum's tools for AI agents (skills folders live here too).</summary>
    internal AssistantTools Tools => _tools;

    /// <summary>Whether the AI panel is shown (View > AI access, Ctrl+Shift+A); where it sits is <see cref="Layout"/>.</summary>
    public bool IsPanelOpen
    {
        get => _layout.IsOpen;
        set => _layout.IsOpen = value;
    }

    /// <summary>Docked right or at the bottom, and its size.</summary>
    public ViewModels.DetailPaneLayoutViewModel Layout => _layout;

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
            OnPanelShown();
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
    public IReadOnlyList<string> AgentNames { get; } = ["Claude Code", "GitHub Copilot CLI", "Cursor CLI", "OpenAI Codex CLI", "Custom agent"];

    /// <summary>The index of "Custom agent" in <see cref="AgentNames"/>.</summary>
    public const int CustomAgentIndex = 4;

    /// <summary>Whether the custom agent's command and arguments are shown.</summary>
    public bool IsCustomAgent => SelectedAgent == CustomAgentIndex;

    /// <summary>The custom agent's program (name on PATH or full path).</summary>
    public string CustomCommand
    {
        get => _settings.Value.Assistant.CustomCommand ?? "";
        set
        {
            _settings.Value.Assistant.CustomCommand = value.Trim();
            _settings.Save();
            _agent = null;
            OnPropertyChanged();
            _ = RefreshAgentAsync();
        }
    }

    /// <summary>The custom agent's arguments: {prompt}, {mcpConfig}, {lignum}, {model} and {effort} are filled in.</summary>
    public string CustomArguments
    {
        get => _settings.Value.Assistant.CustomArguments ?? "-p {prompt}";
        set
        {
            _settings.Value.Assistant.CustomArguments = value;
            _settings.Save();
            _agent = null;
            OnPropertyChanged();
            OnModelChoiceChanged(); // {effort} in the template turns the reasoning box on
        }
    }

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
            _listedModels = null;
            ResetUsage();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomAgent));
            OnModelChoiceChanged();
            _ = RefreshAgentAsync();
        }
    }

    /// <summary>The selected agent's saved model and reasoning level, or null.</summary>
    private AgentModelChoice? ModelChoice => _settings.Value.Assistant.Models.FirstOrDefault(m => m.Agent == SelectedAgent);

    /// <summary>The model for the selected agent (ticket 16); empty = the CLI's default. Takes effect on the next question.</summary>
    public string AgentModel
    {
        get => ModelChoice?.Model ?? "";
        set => SaveModelChoice(value.Trim(), ModelChoice?.Effort);
    }

    /// <summary>The reasoning level for the selected agent; "" = the CLI's default.</summary>
    public string AgentEffort
    {
        get => ModelChoice?.Effort ?? "";
        set => SaveModelChoice(ModelChoice?.Model, value);
    }

    /// <summary>Models from the CLI when available (Cursor); otherwise the agent's built-in suggestions.</summary>
    private IReadOnlyList<string>? _listedModels;

    /// <summary>Model names to offer for the selected agent; any other can be typed.</summary>
    public IReadOnlyList<string> ModelSuggestions => _listedModels ?? CreateAgent(SelectedAgent).SuggestedModels;

    /// <summary>"" (default) and the selected agent's reasoning levels; just "" when it has none.</summary>
    public IReadOnlyList<string> EffortChoices => ["", .. CreateAgent(SelectedAgent).EffortLevels];

    /// <summary>Whether the selected agent takes a reasoning level (the box is hidden otherwise).</summary>
    public bool SupportsEffort => EffortChoices.Count > 1 && CreateAgent(SelectedAgent).AcceptsEffort(AgentModel);

    private void SaveModelChoice(string? model, string? effort)
    {
        var models = _settings.Value.Assistant.Models;
        models.RemoveAll(m => m.Agent == SelectedAgent);
        if (!string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(effort))
            models.Add(new AgentModelChoice { Agent = SelectedAgent, Model = model, Effort = effort });
        _settings.Save();
        if (_agent is not null)
        {
            _agent.Model = model;
            _agent.Effort = effort;
        }

        OnPropertyChanged(nameof(AgentModel));
        OnPropertyChanged(nameof(AgentEffort));
        OnPropertyChanged(nameof(SupportsEffort)); // Copilot's "auto" takes no reasoning level
    }

    private void OnModelChoiceChanged()
    {
        OnPropertyChanged(nameof(AgentModel));
        OnPropertyChanged(nameof(AgentEffort));
        OnPropertyChanged(nameof(ModelSuggestions));
        OnPropertyChanged(nameof(EffortChoices));
        OnPropertyChanged(nameof(SupportsEffort));
    }

    /// <summary>Where the selected agent stands: checking, not installed, not signed in, or ready to chat.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAgentSetup), nameof(ShowAgentIcons), nameof(IsAgentInstalled), nameof(AgentStatus))]
    private AgentState _agentState = AgentState.Unknown;

    /// <summary>Whether the selected agent is installed on this PC.</summary>
    public bool IsAgentInstalled => AgentState is AgentState.NotSignedIn or AgentState.Ready;

    /// <summary>The warning with Install or Sign in, shown until the agent is ready.</summary>
    public bool ShowAgentSetup => AgentState is AgentState.NotInstalled or AgentState.NotSignedIn;

    /// <summary>Once ready, Sign in and Check again are small icons in the header.</summary>
    public bool ShowAgentIcons => AgentState == AgentState.Ready;

    /// <summary>What the warning says.</summary>
    public string AgentStatus
    {
        get
        {
            var agent = CreateAgent(SelectedAgent);
            return AgentState switch
            {
                AgentState.NotInstalled => $"{agent.Name} is not installed. {agent.InstallHint}",
                AgentState.NotSignedIn => $"You are not signed in to {agent.Name}. Press Sign in and finish the sign-in in the window that opens, then Check again.",
                AgentState.Checking => $"Checking {agent.Name}…",
                _ => "",
            };
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
            _ = RefreshAgentAsync();
            return;
        }

        Add(ChatEntryKind.Activity, agent.SignInHint);
        if (!_settings.Value.Assistant.SignedIn.Contains(SelectedAgent))
        {
            _settings.Value.Assistant.SignedIn.Add(SelectedAgent); // for agents that cannot be asked (Copilot CLI)
            _settings.Save();
        }

        StartProcess(start);
    }

    /// <summary>"Check again": is the agent installed, and signed in? Also runs when the panel opens.</summary>
    [RelayCommand]
    private Task RefreshAgent() => RefreshAgentAsync();

    private async Task RefreshAgentAsync()
    {
        var index = SelectedAgent;
        var agent = CreateAgent(index);
        AgentState = AgentState.Checking;
        AgentState state;
        IReadOnlyList<string>? listed = null;
        if (agent.FindExecutable() is null)
        {
            state = AgentState.NotInstalled;
        }
        else
        {
            var signedIn = await agent.IsSignedInAsync(CancellationToken.None);
            state = (signedIn ?? _settings.Value.Assistant.SignedIn.Contains(index)) ? AgentState.Ready : AgentState.NotSignedIn;
            if (state == AgentState.Ready) listed = await agent.ListModelsAsync(CancellationToken.None);
        }

        if (index != SelectedAgent) return; // the user picked another agent meanwhile
        AgentState = state;
        _listedModels = listed;
        OnPropertyChanged(nameof(ModelSuggestions));
    }

    /// <summary>Looks at the agent again when the panel opens with AI access on.</summary>
    private void OnPanelShown()
    {
        if (IsPanelOpen && IsEnabled) _ = RefreshAgentAsync();
    }

    /// <summary>A chat that failed because the agent is not signed in turns the warning back on.</summary>
    private void NoticeSignInError(string message)
    {
        string[] hints = ["log in", "login", "logged in", "sign in", "signed in", "authenticat", "unauthorized", "/login"];
        if (!hints.Any(h => message.Contains(h, StringComparison.OrdinalIgnoreCase))) return;
        if (_settings.Value.Assistant.SignedIn.Remove(SelectedAgent)) _settings.Save();
        AgentState = AgentState.NotSignedIn;
    }

    /// <summary>Starts a window (installer, sign-in terminal); tests replace it.</summary>
    internal Action<System.Diagnostics.ProcessStartInfo> StartProcess { get; set; } = start =>
    {
        try { System.Diagnostics.Process.Start(start)?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { } // the user cancelled, or the program is gone
    };

    /// <summary>Creates the agent for a conversation (tests replace it).</summary>
    internal Func<int, string, CliAgent>? AgentFactory { get; set; }

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
            "codex" => $"codex mcp add lignum -- \"{ExePath}\" {string.Join(' ', McpArguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}",
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
        ResetUsage();
    }

    private bool CanSend() => IsEnabled && !IsBusy && !string.IsNullOrWhiteSpace(Input);

    /// <summary>Asks the selected agent; it uses Lignum's tools through MCP and its answer streams in.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        var question = Input.Trim();
        var prompt = question;
        if (SkillLibrary.ParseCommand(question) is { } command)
        {
            if (IsChatCommand(command.Name))
            {
                RunChatCommand(command.Name, command.Text);
                return;
            }

            var skill = LoadSlashEntries().FirstOrDefault(s => s.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase));
            if (skill is null)
            {
                Add(ChatEntryKind.Error, $"There is no skill /{command.Name}. Type / to see the skills, or add one under Skills.");
                return; // the input stays, to fix the name
            }

            // The agent's own skill goes as typed ("/name text"): the agent expands it. Lignum's are fetched with get_skill.
            if (skill.Scope != SkillScope.Agent) prompt = SkillLibrary.PromptFor(skill, command.Text);
        }

        Input = "";
        IsSkillListOpen = false;
        Add(ChatEntryKind.User, question);
        var answer = new ChatEntry(ChatEntryKind.Assistant, "");
        Entries.Add(answer);
        _agent ??= CreateAgent(SelectedAgent);
        var agent = _agent;
        _cancel = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            await agent.RunTurnAsync(prompt, text => OnUi(() => answer.Text += text), _cancel.Token);
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
            NoticeSignInError(ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cancel.Dispose();
            _cancel = null;
            OnUi(() => CountUsage(agent));
        }
    }

    /// <summary>The last turn's tokens (ticket 20); null when the agent does not report them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsageText))]
    private TokenUsage? _lastTurnUsage;

    /// <summary>All turns of this conversation that reported tokens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsageText))]
    private TokenUsage? _conversationUsage;

    /// <summary>Whether a turn has run in this conversation (before that the usage line is empty).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsageText))]
    private bool _hasTurns;

    /// <summary>The usage line under the chat: last turn, conversation and context window, or why there is none.</summary>
    public string UsageText => !HasTurns ? ""
        : ConversationUsage is not { } total ? $"Token usage: not reported by {AgentNames[SelectedAgent]}"
        : FormatUsage(LastTurnUsage, total);

    internal static string FormatUsage(TokenUsage? last, TokenUsage total)
    {
        static string K(long tokens) => tokens >= 10_000 ? $"{tokens / 1000.0:0}k" : tokens >= 1000 ? $"{tokens / 1000.0:0.0}k" : $"{tokens}";
        var parts = new List<string>();
        if (last is not null) parts.Add($"Last: {K(last.AllInput)} in, {K(last.Output)} out");
        parts.Add($"Conversation: {K(total.AllInput)} in, {K(total.Output)} out"
                  + (total.CostUsd is { } cost ? $" (${cost:0.00})" : ""));
        if (total.ContextUsed is { } used && total.ContextWindow is { } window && window > 0)
            parts.Add($"Context: {K(used)} of {K(window)} ({K(Math.Max(0, window - used))} left)");
        return string.Join(" · ", parts);
    }

    private void CountUsage(CliAgent agent)
    {
        if (!ReferenceEquals(agent, _agent)) return; // a new conversation started meanwhile
        HasTurns = true;
        LastTurnUsage = agent.LastTurnUsage;
        if (agent.LastTurnUsage is { } turn) ConversationUsage = ConversationUsage?.Plus(turn) ?? turn;
    }

    private void ResetUsage()
    {
        HasTurns = false;
        LastTurnUsage = null;
        ConversationUsage = null;
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop() => _cancel?.Cancel();

    private CliAgent CreateAgent(int index)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lignum",
            "agents", Guid.NewGuid().ToString("N"));
        var made = AgentFactory?.Invoke(index, folder) ?? index switch
        {
            1 => new CopilotCliAgent(),
            2 => new CursorCliAgent(),
            3 => new CodexCliAgent(),
            CustomAgentIndex => new CustomAgent(CustomCommand, CustomArguments),
            _ => new ClaudeCodeAgent(),
        };
        if (made.WorkFolder.Length == 0) made.WorkFolder = folder;
        if (_settings.Value.Assistant.Models.FirstOrDefault(m => m.Agent == index) is { } choice)
        {
            made.Model = choice.Model;
            made.Effort = choice.Effort;
        }
        if (made.LignumExe.Length == 0)
        {
            made.LignumExe = ExePath;
            made.LignumArguments = McpArguments;
        }

        return made;
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
        "select_text" => $"Selecting text on line {Arg(call, "line")}",
        "list_tabs" => "Listing the open files",
        "select_tab" => $"Switching to tab {Arg(call, "tab")}",
        "search_all_tabs" => $"Searching all tabs for \"{Arg(call, "pattern")}\"",
        "find_in_files" => $"Searching files in {Arg(call, "folder")} for \"{Arg(call, "pattern")}\"",
        "show_search" => $"Showing a search for \"{Arg(call, "pattern")}\"",
        "list_filter_rules" => "Listing the filter rules",
        "remove_filter_rule" => $"Removing filter rule {Arg(call, "index")}",
        "set_filter_rule_enabled" => $"Turning filter rule {Arg(call, "index")} {(Arg(call, "enabled") == "True" ? "on" : "off")}",
        "clear_filter_rules" => "Removing every filter rule",
        "list_bookmarks" => "Listing the bookmarks",
        "remove_bookmark" => Arg(call, "all") == "True" ? "Removing every bookmark" : $"Removing the bookmark on line {Arg(call, "line")}",
        "edit_bookmark" => $"Editing the bookmark on line {Arg(call, "line")}",
        "get_selection" => "Reading your selection",
        "set_hide_non_matching" => Arg(call, "enabled") == "True" ? "Hiding non-matching lines" : "Showing every line",
        "list_highlight_rules" => "Listing the highlight rules",
        "remove_highlight_rule" => $"Removing highlight rule {Arg(call, "index")}",
        "clear_highlight_rules" => "Removing every highlight rule",
        "show_full_line" => $"Showing line {Arg(call, "line")} in full",
        "open_file" => $"Opening {Arg(call, "path")}",
        "close_tab" => $"Closing tab {Arg(call, "tab")}",
        "export_package" => $"Exporting to {Arg(call, "path")}",
        "list_skills" => "Listing the skills",
        "get_skill" => $"Reading skill /{Arg(call, "name")}",
        "save_skill" => $"Saving skill /{Arg(call, "name")} ({(Arg(call, "scope") is "global" ? "global" : "local")})",
        "list_dir" => $"Listing {Arg(call, "path")}",
        "read_file" => $"Reading {Arg(call, "path")}",
        "write_file" => $"Writing {Arg(call, "path")}",
        "edit_file" => $"Editing {Arg(call, "path")}",
        "run_command" => $"Running {(Arg(call, "shell") is { Length: > 0 } shell and not "none" ? shell + ": " : "")}{Arg(call, "command")}",
        _ => call.Name,
    };

    private static string Arg(ToolCall call, string name) =>
        call.Input.TryGetValue(name, out var value) ? value.ToString() : "?";
}
