using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lignum.App.Assistant;

/// <summary>Where the chat's agent stands, for the AI panel's setup warning.</summary>
public enum AgentState
{
    Unknown,
    Checking,
    NotInstalled,
    NotSignedIn,
    Ready,
}

/// <summary>
/// An AI agent CLI the user already has (ticket 05): Lignum runs it in its non-interactive mode for the chat in
/// the AI panel, attaches Lignum's MCP tools, and shows its answer. The agent runs on the user's own
/// subscription. One instance holds one conversation.
/// </summary>
public abstract class CliAgent
{
    /// <summary>What the agent is told about where it runs (Claude Code gets it as a system prompt addition).</summary>
    protected const string Instructions =
        "You are the assistant in Lignum, a read-only viewer for very large log files. The user is looking at log " +
        "files open in Lignum. Use the lignum MCP tools to look at them (list_tabs, search, search_all_tabs, read_lines, " +
        "find_in_files for folders) instead of " +
        "guessing, and never invent log content. Line numbers are 1-based. Search with a precise pattern first, then " +
        "read the lines around what matters. Write regular expressions in .NET syntax and check them with search. You " +
        "may add filter rules, highlight rules and bookmarks and go to lines when that helps; say what you changed. " +
        "Keep answers short and concrete, and quote line numbers.";

    /// <summary>"Claude Code" or "GitHub Copilot CLI".</summary>
    public abstract string Name { get; }

    /// <summary>The command names to look for on PATH, e.g. "claude".</summary>
    public abstract string Command { get; }

    /// <summary>How to install it, shown when it is not found.</summary>
    public abstract string InstallHint { get; }

    /// <summary>The vendor's official install command, run in a visible PowerShell window by "Install".</summary>
    public abstract string InstallCommand { get; }

    /// <summary>What to do in the sign-in window ("Sign in" runs the agent in a terminal).</summary>
    public abstract string SignInHint { get; }

    /// <summary>Where the official installers put the program, for when PATH is not updated yet.</summary>
    protected abstract IEnumerable<string> KnownLocations { get; }

    /// <summary>The installed program: on PATH (as Windows has it now, not as it was when Lignum started) or in a known location.</summary>
    public string? FindExecutable() =>
        Executable ?? FindOnPath(Command) ?? KnownLocations.FirstOrDefault(File.Exists);

    /// <summary>Whether the user is signed in to the agent; null when the agent cannot tell (the panel then remembers).</summary>
    public virtual Task<bool?> IsSignedInAsync(CancellationToken cancellationToken) => Task.FromResult<bool?>(null);

    /// <summary>Opens a PowerShell window that runs <see cref="InstallCommand"/> and stays open.</summary>
    public ProcessStartInfo InstallStart() => new("powershell.exe")
    {
        UseShellExecute = true,
        ArgumentList =
        {
            "-NoExit", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
            $"Write-Host 'Installing {Name}...'; {InstallCommand}; Write-Host ''; " +
            $"Write-Host 'Done. Close this window, then press Check again in Lignum.'",
        },
    };

    /// <summary>Opens the agent in its own terminal window to sign in; null when it is not installed.</summary>
    public ProcessStartInfo? SignInStart()
    {
        if (FindExecutable() is not { } exe) return null;
        var start = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        foreach (var argument in SignInArguments) start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>Arguments for the sign-in window (e.g. "login"); none starts the agent itself.</summary>
    protected virtual IReadOnlyList<string> SignInArguments => [];

    /// <summary>Runs the agent with <paramref name="arguments"/>: true for exit code 0, null when it could not tell.</summary>
    protected async Task<bool?> ExitCodeIsZeroAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (FindExecutable() is not { } exe) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            Directory.CreateDirectory(WorkFolder);
            return await ProcessRunner.RunAsync(exe, arguments, null, WorkFolder, _ => { }, _ => { }, timeout.Token) == 0;
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    /// <summary>One line without double quotes, for prompts that go on the command line (cmd.exe shims).</summary>
    protected static string OneLine(string text) => text.Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>Whether a turn was already sent in this conversation.</summary>
    protected bool HasStarted { get; private set; }

    /// <summary>A folder of the agent's own (MCP config, prompt file, working directory); created by the caller.</summary>
    public string WorkFolder { get; set; } = "";

    /// <summary>The MCP server entry that starts Lignum.exe --mcp.</summary>
    public string LignumExe { get; set; } = "";

    public IReadOnlyList<string> LignumArguments { get; set; } = [];

    /// <summary>The program to run, or null when the agent is not installed. Overridable for tests.</summary>
    public string? Executable { get; init; }

    /// <summary>Runs one turn: sends <paramref name="prompt"/> and streams the answer's text to <paramref name="onText"/>.</summary>
    public async Task RunTurnAsync(string prompt, Action<string> onText, CancellationToken cancellationToken)
    {
        var exe = FindExecutable() ?? throw new AgentException($"{Name} is not installed. {InstallHint}");
        Directory.CreateDirectory(WorkFolder);
        var arguments = BuildArguments(prompt);
        var output = new StringBuilder();
        var errors = new StringBuilder();
        var exitCode = await ProcessRunner.RunAsync(exe, arguments, PromptOnStdin ? prompt : null, WorkFolder,
            line => OnOutputLine(line, onText, output), line => errors.AppendLine(line), cancellationToken);
        HasStarted = true;
        Finish(onText, output);
        if (exitCode != 0 && FailureText(errors.ToString()) is { } failure) throw new AgentException(failure);
    }

    /// <summary>The arguments for the next turn (the first, or a follow-up in the same conversation).</summary>
    protected internal abstract IReadOnlyList<string> BuildArguments(string prompt);

    /// <summary>True when the prompt goes to the agent's standard input instead of the command line.</summary>
    protected virtual bool PromptOnStdin => false;

    protected abstract void OnOutputLine(string line, Action<string> onText, StringBuilder output);

    /// <summary>Called when the process has exited; can send text that only arrives at the end.</summary>
    protected virtual void Finish(Action<string> onText, StringBuilder output)
    {
    }

    protected virtual string? FailureText(string errors) =>
        string.IsNullOrWhiteSpace(errors) ? $"{Name} stopped with an error." : $"{Name}: {errors.Trim()}";

    /// <summary>Writes a file in <see cref="WorkFolder"/> and returns its path.</summary>
    protected string WriteFile(string name, string content)
    {
        var path = Path.Combine(WorkFolder, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Finds <paramref name="command"/> on PATH with the usual Windows extensions (.exe, .cmd, ...). Also reads the
    /// user and machine PATH from Windows, so a program installed after Lignum started is found too.
    /// </summary>
    public static string? FindOnPath(string command)
    {
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var path = string.Join(';',
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        foreach (var folder in Environment.ExpandEnvironmentVariables(path).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(folder.Trim('"'), command + extension);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry.
                }
            }
        }

        return null;
    }
}

/// <summary>Claude Code (`claude`), on the user's Claude subscription. Only Lignum's tools are available to it.</summary>
public sealed class ClaudeCodeAgent : CliAgent
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private bool _sawText;

    public override string Name => "Claude Code";

    public override string Command => "claude";

    public override string InstallHint => "Press Install to run Anthropic's installer, then Sign in (needs a Claude Pro, Max, Team or Enterprise plan).";

    public override string InstallCommand => "irm https://claude.ai/install.ps1 | iex";

    public override string SignInHint => "Claude Code opens in a terminal: follow the browser sign-in, then close the window.";

    protected override IEnumerable<string> KnownLocations =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"),
    ];

    protected override bool PromptOnStdin => true;

    /// <summary><c>claude auth status</c> exits with 0 when signed in, 1 when not.</summary>
    public override Task<bool?> IsSignedInAsync(CancellationToken cancellationToken) =>
        ExitCodeIsZeroAsync(["auth", "status"], cancellationToken);

    protected internal override IReadOnlyList<string> BuildArguments(string prompt)
    {
        var mcp = WriteFile("claude-mcp.json", JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object> { ["lignum"] = new { command = LignumExe, args = LignumArguments } },
        }));
        var instructions = WriteFile("instructions.txt", Instructions);
        List<string> arguments =
        [
            "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
            "--mcp-config", mcp, "--strict-mcp-config",
            "--tools", "", // no file, shell or web tools: only Lignum's
            "--allowedTools", "mcp__lignum",
            "--append-system-prompt-file", instructions,
        ];
        arguments.AddRange(HasStarted ? ["--resume", _sessionId] : ["--session-id", _sessionId]);
        return arguments;
    }

    protected override void OnOutputLine(string line, Action<string> onText, StringBuilder output)
    {
        if (line.Length == 0 || line[0] != '{') return;
        JsonElement message;
        try
        {
            message = JsonDocument.Parse(line).RootElement;
        }
        catch (JsonException)
        {
            return;
        }

        switch (Str(message, "type"))
        {
            case "stream_event" when message.TryGetProperty("event", out var e) && Str(e, "type") == "content_block_delta"
                                     && e.TryGetProperty("delta", out var delta) && Str(delta, "type") == "text_delta":
                var text = Str(delta, "text") ?? "";
                _sawText |= text.Length > 0;
                onText(text);
                break;
            case "result":
                if (message.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True)
                    throw new AgentException($"Claude Code: {Str(message, "result") ?? Str(message, "subtype") ?? "error"}");
                output.Append(Str(message, "result"));
                break;
        }
    }

    protected override void Finish(Action<string> onText, StringBuilder output)
    {
        if (!_sawText && output.Length > 0) onText(output.ToString()); // no partial messages: show the final answer
        _sawText = false;
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// GitHub Copilot CLI (`copilot`), on the user's Copilot subscription. Its prompt mode prints plain text; the
/// conversation continues with --continue in this conversation's own working folder.
/// </summary>
public sealed class CopilotCliAgent : CliAgent
{
    public override string Name => "GitHub Copilot CLI";

    public override string Command => "copilot";

    public override string InstallHint => "Press Install to install it with WinGet, then Sign in (needs a GitHub Copilot subscription).";

    public override string InstallCommand => "winget install GitHub.Copilot";

    public override string SignInHint => "Copilot CLI opens in a terminal: type /login, follow the steps, then close the window.";

    protected override IEnumerable<string> KnownLocations =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "copilot.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "copilot.cmd"),
    ];

    protected internal override IReadOnlyList<string> BuildArguments(string prompt)
    {
        var mcp = WriteFile("copilot-mcp.json", JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["lignum"] = new { type = "local", command = LignumExe, args = LignumArguments, tools = new[] { "*" } },
            },
        }));
        // The prompt goes on the command line; keep it on one line and free of quotes for cmd.exe-based shims.
        var text = (HasStarted ? prompt : $"{Instructions}\n\nThe user asks: {prompt}")
            .Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ');
        List<string> arguments = ["-p", text, "--additional-mcp-config", "@" + mcp, "--allow-tool", "lignum", "--no-color"];
        if (HasStarted) arguments.Add("--continue");
        return arguments;
    }

    protected override void OnOutputLine(string line, Action<string> onText, StringBuilder output)
    {
        output.AppendLine(line);
        onText(line + "\n");
    }
}

/// <summary>
/// Cursor CLI (`agent`), on the user's Cursor subscription. Lignum's MCP server goes in the conversation folder's
/// .cursor/mcp.json (Cursor reads project MCP settings) and is approved with --approve-mcps.
/// </summary>
public sealed class CursorCliAgent : CliAgent
{
    public override string Name => "Cursor CLI";

    public override string Command => "agent";

    public override string InstallHint => "Press Install to run Cursor's installer, then Sign in (needs a Cursor account).";

    public override string InstallCommand => "irm 'https://cursor.com/install?win32=true' | iex";

    public override string SignInHint => "Cursor CLI opens in a terminal: sign in when it asks, then close the window.";

    protected override IEnumerable<string> KnownLocations =>
    [
        .. new[] { "agent", "cursor-agent" }.Select(FindOnPath).OfType<string>(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "agent.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cursor-agent", "agent.cmd"),
    ];

    protected internal override IReadOnlyList<string> BuildArguments(string prompt)
    {
        Directory.CreateDirectory(Path.Combine(WorkFolder, ".cursor"));
        WriteFile(Path.Combine(".cursor", "mcp.json"), JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object> { ["lignum"] = new { command = LignumExe, args = LignumArguments } },
        }));
        List<string> arguments = ["-p", OneLine(HasStarted ? prompt : $"{Instructions}\n\nThe user asks: {prompt}"),
                                  "--approve-mcps", "--output-format", "text"];
        if (HasStarted) arguments.Add("--continue");
        return arguments;
    }

    protected override void OnOutputLine(string line, Action<string> onText, StringBuilder output)
    {
        output.AppendLine(line);
        onText(line + "\n");
    }
}

/// <summary>
/// OpenAI Codex CLI (`codex`), on the user's ChatGPT plan. Runs `codex exec` in a read-only sandbox with Lignum's MCP
/// server added through -c overrides; a follow-up resumes the last session in the conversation folder.
/// </summary>
public sealed class CodexCliAgent : CliAgent
{
    public override string Name => "OpenAI Codex CLI";

    public override string Command => "codex";

    public override string InstallHint => "Press Install to install it with npm (needs Node.js from nodejs.org), then Sign in with your ChatGPT account.";

    public override string InstallCommand => "npm install -g @openai/codex";

    public override string SignInHint => "Codex runs `codex login` in a terminal: sign in with ChatGPT in the browser, then close the window.";

    protected override IReadOnlyList<string> SignInArguments => ["login"];

    protected override IEnumerable<string> KnownLocations =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd"),
    ];

    /// <summary><c>codex login status</c> exits with 0 when logged in.</summary>
    public override Task<bool?> IsSignedInAsync(CancellationToken cancellationToken) =>
        ExitCodeIsZeroAsync(["login", "status"], cancellationToken);

    protected internal override IReadOnlyList<string> BuildArguments(string prompt)
    {
        List<string> arguments = ["exec"];
        if (HasStarted) arguments.AddRange(["resume", "--last"]);
        arguments.AddRange(
        [
            "--skip-git-repo-check", "--sandbox", "read-only",
            "-c", "mcp_servers.lignum.command=" + JsonSerializer.Serialize(LignumExe),
            "-c", "mcp_servers.lignum.args=" + JsonSerializer.Serialize(LignumArguments),
            OneLine(HasStarted ? prompt : $"{Instructions}\n\nThe user asks: {prompt}"),
        ]);
        return arguments;
    }

    protected override void OnOutputLine(string line, Action<string> onText, StringBuilder output)
    {
        output.AppendLine(line);
        onText(line + "\n");
    }

    protected override string? FailureText(string errors) =>
        string.IsNullOrWhiteSpace(errors) ? "OpenAI Codex CLI stopped with an error." : $"OpenAI Codex CLI: {errors.Trim().Split('\n')[^1]}";
}

/// <summary>
/// Any other agent CLI: the user gives the command and an argument template. {prompt} is the question, {mcpConfig}
/// the path of an MCP settings file (mcpServers JSON) with Lignum in it, {lignum} the Lignum.exe path. The answer is
/// read as plain text from standard output; each question starts afresh unless the template continues a session.
/// </summary>
public sealed class CustomAgent(string command, string argumentTemplate) : CliAgent
{
    public override string Name => "Custom agent";

    public override string Command => command;

    public override string InstallHint => "Enter the agent's command and its arguments below.";

    public override string InstallCommand => "Write-Host 'Install your agent the way its documentation says.'";

    public override string SignInHint => "Your agent opens in a terminal: sign in the way it asks, then close the window.";

    protected override IEnumerable<string> KnownLocations => File.Exists(command) ? [command] : [];

    protected internal override IReadOnlyList<string> BuildArguments(string prompt)
    {
        var mcp = WriteFile("mcp.json", JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object> { ["lignum"] = new { command = LignumExe, args = LignumArguments } },
        }));
        var question = OneLine(HasStarted ? prompt : $"{Instructions}\n\nThe user asks: {prompt}");
        return [.. SplitArguments(argumentTemplate).Select(a => a
            .Replace("{prompt}", question).Replace("{mcpConfig}", mcp).Replace("{lignum}", LignumExe))];
    }

    protected override void OnOutputLine(string line, Action<string> onText, StringBuilder output)
    {
        output.AppendLine(line);
        onText(line + "\n");
    }

    /// <summary>Splits a command line on spaces, keeping "quoted parts" together.</summary>
    internal static List<string> SplitArguments(string template)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in template)
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any || current.Length > 0) result.Add(current.ToString());
                current.Clear();
                any = false;
                continue;
            }

            current.Append(c);
        }

        if (any || current.Length > 0) result.Add(current.ToString());
        return result;
    }
}

/// <summary>An agent could not run or answered with an error; the message is shown in the panel.</summary>
public sealed class AgentException(string message) : Exception(message);

/// <summary>Starts a process with arguments, feeds stdin, streams stdout and stderr lines, and kills it on cancel.</summary>
internal static class ProcessRunner
{
    public static async Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, string? stdin,
                                           string workingDirectory, Action<string> onStdout, Action<string> onStderr,
                                           CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (Path.GetExtension(executable) is ".cmd" or ".bat")
        {
            // npm installs CLIs as .cmd shims, which only cmd.exe can run.
            start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            start.Arguments = "/d /s /c \"" + Quote(executable) + " " + string.Join(" ", arguments.Select(Quote)) + "\"";
        }
        else
        {
            start.FileName = executable;
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new AgentException($"Could not start {executable}.");
        using var kill = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { } // already exited
        });
        await process.StandardInput.WriteAsync(stdin ?? "");
        process.StandardInput.Close();

        var stdout = Pump(process.StandardOutput, onStdout);
        var stderr = Pump(process.StandardError, onStderr);
        await Task.WhenAll(stdout, stderr);
        await process.WaitForExitAsync(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static async Task Pump(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line) onLine(line);
    }

    private static string Quote(string argument) =>
        argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c is '"' or '&' or '|' or '<' or '>' or '^' or '(' or ')')
            ? argument
            : "\"" + argument.Replace("\"", "\\\"") + "\"";
}
