using Lignum.App.Assistant;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>The in-app chat (ticket 05): it runs the user's agent CLI (Claude Code or Copilot CLI) with Lignum's MCP tools.</summary>
public sealed class AgentChatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public AgentChatTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string Folder(string name) => Path.Combine(_dir, name);

    /// <summary>A stand-in for claude.cmd: records its arguments and prints Claude Code's stream-json output.</summary>
    private string FakeClaude(params string[] jsonLines)
    {
        var path = Path.Combine(_dir, "claude.cmd");
        File.WriteAllLines(path,
        [
            "@echo off",
            "echo %*>> \"%~dp0args.txt\"",
            "more > \"%~dp0stdin.txt\"",
            .. jsonLines.Select(line => "echo " + line.Replace("%", "%%")),
        ]);
        return path;
    }

    private MainViewModel Start()
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        main.Assistant.IsEnabled = true;
        return main;
    }

    private static async Task Ask(AssistantViewModel assistant, string question)
    {
        assistant.Input = question;
        await assistant.SendCommand.ExecuteAsync(null);
    }

    [Fact]
    public void Claude_Code_gets_only_Lignums_tools_and_continues_the_same_session()
    {
        var agent = new ClaudeCodeAgent { WorkFolder = Folder("claude"), LignumExe = @"C:\L\Lignum.exe", LignumArguments = ["--mcp"] };
        Directory.CreateDirectory(agent.WorkFolder);

        var first = agent.BuildArguments("hi");

        Assert.Equal(["-p", "--output-format", "stream-json"], first.Take(3));
        Assert.Contains("--strict-mcp-config", first);
        Assert.Equal("", first[first.ToList().IndexOf("--tools") + 1]); // no built-in tools
        Assert.Equal("mcp__lignum", first[first.ToList().IndexOf("--allowedTools") + 1]);
        var mcp = File.ReadAllText(first[first.ToList().IndexOf("--mcp-config") + 1]);
        Assert.Contains("\"command\":\"C:\\\\L\\\\Lignum.exe\"", mcp);
        Assert.Contains("\"args\":[\"--mcp\"]", mcp);
        Assert.Contains("--session-id", first);
    }

    [Fact]
    public void Copilot_gets_a_one_line_prompt_and_continues_with_continue()
    {
        var agent = new CopilotCliAgent { WorkFolder = Folder("copilot"), LignumExe = "Lignum.exe", LignumArguments = ["--mcp"] };
        Directory.CreateDirectory(agent.WorkFolder);

        var arguments = agent.BuildArguments("Find \"timeout\"\nerrors");

        var prompt = arguments[arguments.ToList().IndexOf("-p") + 1];
        Assert.DoesNotContain('"', prompt);
        Assert.DoesNotContain('\n', prompt);
        Assert.EndsWith("The user asks: Find 'timeout' errors", prompt);
        Assert.Equal("lignum", arguments[arguments.ToList().IndexOf("--allow-tool") + 1]);
        Assert.StartsWith("@", arguments[arguments.ToList().IndexOf("--additional-mcp-config") + 1]);
        Assert.DoesNotContain("--continue", arguments);
    }

    [Fact]
    public async Task A_saved_chat_is_listed_and_continuing_it_resumes_the_same_agent_session()
    {
        var exe = FakeClaude("""{"type":"result","subtype":"success","is_error":false,"result":"An answer.","session_id":"s"}""");
        var first = Start();
        first.Assistant.AgentFactory = (_, folder) => new ClaudeCodeAgent
            { WorkFolder = folder, LignumExe = "", LignumArguments = [], Executable = exe };
        await Ask(first.Assistant, "Any errors?");

        var later = Start(); // a later run of Lignum, same settings folder
        later.Assistant.AgentFactory = first.Assistant.AgentFactory;
        later.Assistant.RefreshChatHistoryCommand.Execute(null);
        var saved = Assert.Single(later.Assistant.ChatHistory);
        Assert.Equal("Any errors?", saved.Title);

        later.Assistant.OpenChatCommand.Execute(saved);
        Assert.Contains(later.Assistant.Entries, e => e.Kind == ChatEntryKind.Assistant && e.Text == "An answer.");
        await Ask(later.Assistant, "And warnings?");

        var calls = File.ReadAllLines(Path.Combine(_dir, "args.txt"));
        var session = calls[0].Split(' ')[calls[0].Split(' ').ToList().IndexOf("--session-id") + 1];
        Assert.Contains($"--resume {session}", calls[1]);
        later.Assistant.RefreshChatHistoryCommand.Execute(null);
        Assert.Single(later.Assistant.ChatHistory); // continued, not a new chat

        later.Assistant.DeleteChatCommand.Execute(later.Assistant.ChatHistory[0]);
        later.Assistant.RefreshChatHistoryCommand.Execute(null);
        Assert.Empty(later.Assistant.ChatHistory);
    }

    [Fact]
    public void An_agent_that_cannot_resume_gets_the_earlier_turns_newest_kept_within_the_limits()
    {
        var lines = Enumerable.Range(1, 30).SelectMany(i => new[]
        {
            new ChatLine(ChatEntryKind.User, $"q{i}"),
            new ChatLine(ChatEntryKind.Activity, "Searching"),
            new ChatLine(ChatEntryKind.Assistant, $"a{i}"),
        }).ToList();

        var preamble = ChatHistoryStore.ContextPreamble(lines, maxTurns: 20);
        Assert.StartsWith("Conversation so far:", preamble);
        Assert.DoesNotContain("User: q10\n", preamble);
        Assert.Contains("User: q11", preamble);
        Assert.Contains("Assistant: a30", preamble);
        Assert.DoesNotContain("Searching", preamble);
        Assert.EndsWith("New question: ", preamble);

        var small = ChatHistoryStore.ContextPreamble(lines, maxChars: 40);
        Assert.Contains("Assistant: a30", small);
        Assert.DoesNotContain("q1\n", small);
        Assert.Equal("", ChatHistoryStore.ContextPreamble([]));
        Assert.False(new CustomAgent("x", "-p {prompt}").CanResume);
    }

    [Fact]
    public async Task The_answer_streams_into_the_panel_and_the_follow_up_resumes_the_session()
    {
        var exe = FakeClaude(
            """{"type":"system","subtype":"init","session_id":"s"}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"Two errors, "}}}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"lines 2 and 4."}}}""",
            """{"type":"result","subtype":"success","is_error":false,"result":"Two errors, lines 2 and 4.","session_id":"s"}""");
        var main = Start();
        main.Assistant.AgentFactory = (_, folder) => new ClaudeCodeAgent
            { WorkFolder = folder, LignumExe = "", LignumArguments = [], Executable = exe };

        await Ask(main.Assistant, "Any errors?");
        await Ask(main.Assistant, "And warnings?");

        var texts = main.Assistant.Entries.Select(e => $"{e.Kind}: {e.Text}").ToList();
        Assert.Contains("User: Any errors?", texts);
        Assert.Equal(2, texts.Count(t => t == "Assistant: Two errors, lines 2 and 4."));
        var calls = File.ReadAllLines(Path.Combine(_dir, "args.txt"));
        Assert.Contains("--session-id", calls[0]);
        Assert.Contains("--resume", calls[1]);
        Assert.Contains("And warnings?", File.ReadAllText(Path.Combine(_dir, "stdin.txt")));
        Assert.False(main.Assistant.IsBusy);
    }

    [Fact]
    public async Task Claude_Codes_token_usage_is_shown_per_turn_and_per_conversation_with_the_context_window()
    {
        var exe = FakeClaude(
            """{"type":"assistant","message":{"usage":{"input_tokens":10,"cache_read_input_tokens":40000,"cache_creation_input_tokens":2000,"output_tokens":300}}}""",
            """{"type":"result","subtype":"success","is_error":false,"result":"ok","total_cost_usd":0.25,"usage":{"input_tokens":20,"cache_read_input_tokens":80000,"cache_creation_input_tokens":2000,"output_tokens":500},"modelUsage":{"claude-x":{"contextWindow":200000}}}""");
        var main = Start();
        main.Assistant.AgentFactory = (_, folder) => new ClaudeCodeAgent
            { WorkFolder = folder, LignumExe = "", LignumArguments = [], Executable = exe };
        Assert.Equal("", main.Assistant.UsageText);

        await Ask(main.Assistant, "one");
        await Ask(main.Assistant, "two");

        Assert.Equal(new TokenUsage(20, 500, 80000, 2000, 0.25m, 42310, 200000), main.Assistant.LastTurnUsage);
        Assert.Equal(164040, main.Assistant.ConversationUsage!.AllInput);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("Last: 82k in, 500 out · Conversation: 164k in, 1.0k out ($0.50) · Context: 42k of 200k (158k left)",
                main.Assistant.UsageText);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }

        main.Assistant.NewConversationCommand.Execute(null);
        Assert.Equal("", main.Assistant.UsageText);
    }

    [Fact]
    public async Task An_agent_that_reports_no_usage_says_so()
    {
        var main = Start();
        main.Assistant.SelectedAgent = 1;
        var exe = Path.Combine(_dir, "copilot.cmd");
        File.WriteAllLines(exe, ["@echo off", "echo An answer."]);
        main.Assistant.AgentFactory = (_, folder) => new CopilotCliAgent
            { WorkFolder = folder, LignumExe = "", LignumArguments = [], Executable = exe };

        await Ask(main.Assistant, "hi");

        Assert.Equal("Token usage: not reported by GitHub Copilot CLI", main.Assistant.UsageText);
    }

    [Fact]
    public void Codex_runs_read_only_with_Lignum_added_and_resumes_the_last_session()
    {
        var agent = new CodexCliAgent { WorkFolder = Folder("codex"), LignumExe = @"C:\L\Lignum.exe", LignumArguments = ["--mcp"] };

        var first = agent.BuildArguments("hi");

        Assert.Equal("exec", first[0]);
        Assert.DoesNotContain("resume", first);
        Assert.Equal("read-only", first[first.ToList().IndexOf("--sandbox") + 1]);
        Assert.Contains("mcp_servers.lignum.command=\"C:\\\\L\\\\Lignum.exe\"", first);
        Assert.Contains("mcp_servers.lignum.args=[\"--mcp\"]", first);
        Assert.EndsWith("The user asks: hi", first[^1]);
    }

    [Fact]
    public void Cursor_gets_Lignum_in_its_project_mcp_file_and_approves_it()
    {
        var agent = new CursorCliAgent { WorkFolder = Folder("cursor"), LignumExe = "Lignum.exe", LignumArguments = ["--mcp"] };
        Directory.CreateDirectory(agent.WorkFolder);

        var arguments = agent.BuildArguments("hi");

        Assert.Contains("--approve-mcps", arguments);
        Assert.Equal("-p", arguments[0]);
        Assert.Contains("\"lignum\"", File.ReadAllText(Path.Combine(agent.WorkFolder, ".cursor", "mcp.json")));
        // Print mode cannot answer per-tool MCP prompts; allowlist lignum tools like Copilot's --allow-tool lignum.
        // Cursor's schema requires both allow and deny arrays.
        var cli = File.ReadAllText(Path.Combine(agent.WorkFolder, ".cursor", "cli.json"));
        Assert.Contains("Mcp(lignum:*)", cli);
        Assert.Contains("\"deny\"", cli);
        Assert.DoesNotContain("--force", arguments);
        Assert.DoesNotContain("--yolo", arguments);
    }

    private T Agent<T>(T agent, string? model, string? effort) where T : CliAgent
    {
        agent.WorkFolder = Folder(typeof(T).Name);
        agent.LignumExe = "Lignum.exe";
        agent.LignumArguments = ["--mcp"];
        agent.Model = model;
        agent.Effort = effort;
        Directory.CreateDirectory(agent.WorkFolder);
        return agent;
    }

    /// <summary>The value right after <paramref name="flag"/>, or null when the flag is not there.</summary>
    private static string? After(IReadOnlyList<string> arguments, string flag)
    {
        var i = arguments.ToList().IndexOf(flag);
        return i < 0 ? null : arguments[i + 1];
    }

    [Fact]
    public void Each_agent_passes_the_chosen_model_and_reasoning_level_with_its_own_flags()
    {
        var claude = Agent(new ClaudeCodeAgent(), "opus", "High").BuildArguments("hi");
        Assert.Equal("opus", After(claude, "--model"));
        Assert.Equal("high", After(claude, "--effort"));

        var copilot = Agent(new CopilotCliAgent(), "gpt-5", "xhigh").BuildArguments("hi");
        Assert.Equal("gpt-5", After(copilot, "--model"));
        Assert.Equal("xhigh", After(copilot, "--reasoning-effort"));

        var cursor = Agent(new CursorCliAgent(), "gpt-5", "high").BuildArguments("hi");
        Assert.Equal("gpt-5", After(cursor, "--model"));
        Assert.DoesNotContain("high", cursor); // Cursor has no reasoning flag
        Assert.Contains("auto", new CursorCliAgent().SuggestedModels); // dropdown is not empty before agent models runs

        var codex = Agent(new CodexCliAgent(), "gpt-5-codex", "medium").BuildArguments("hi");
        Assert.Equal("gpt-5-codex", After(codex, "-m"));
        Assert.Contains("model_reasoning_effort=\"medium\"", codex);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("")]
    public void Copilot_gets_no_reasoning_level_when_it_picks_the_model_itself(string model)
    {
        var copilot = Agent(new CopilotCliAgent(), model, "medium").BuildArguments("hi");

        Assert.DoesNotContain("--reasoning-effort", copilot);
        Assert.DoesNotContain("medium", copilot);
    }

    [Fact]
    public void The_reasoning_box_is_hidden_while_Copilot_picks_the_model_itself()
    {
        var main = Start();
        main.Assistant.SelectedAgent = 1;
        main.Assistant.AgentModel = "auto";
        Assert.False(main.Assistant.SupportsEffort);

        main.Assistant.AgentModel = "gpt-5";
        Assert.True(main.Assistant.SupportsEffort);
    }

    [Fact]
    public void No_model_or_level_or_an_unknown_level_passes_no_flag()
    {
        var claude = Agent(new ClaudeCodeAgent(), " ", "turbo").BuildArguments("hi");

        Assert.DoesNotContain("--model", claude);
        Assert.DoesNotContain("--effort", claude);
        Assert.DoesNotContain("-m", Agent(new CodexCliAgent(), null, null).BuildArguments("hi"));
    }

    [Fact]
    public void A_custom_agent_fills_in_model_and_effort_and_drops_an_empty_one_with_its_flag()
    {
        var both = Agent(new CustomAgent("agy", "-p {prompt} --model {model} --effort {effort}"), "m1", "low").BuildArguments("q");
        Assert.Equal("m1", After(both, "--model"));
        Assert.Equal("low", After(both, "--effort"));

        var none = Agent(new CustomAgent("agy", "-p {prompt} --model {model} --effort {effort}"), "", null).BuildArguments("q");
        Assert.DoesNotContain("--model", none);
        Assert.DoesNotContain("--effort", none);
        Assert.Empty(new CustomAgent("agy", "-p {prompt}").EffortLevels);
    }

    [Fact]
    public void The_model_and_level_are_saved_per_agent()
    {
        var main = Start();
        main.Assistant.SelectedAgent = 0;
        main.Assistant.AgentModel = "sonnet";
        main.Assistant.AgentEffort = "max";
        main.Assistant.SelectedAgent = 2;
        Assert.Equal("", main.Assistant.AgentModel);
        Assert.False(main.Assistant.SupportsEffort);
        main.Assistant.AgentModel = "gpt-5";

        var again = Start();
        again.Assistant.SelectedAgent = 0;
        Assert.Equal("sonnet", again.Assistant.AgentModel);
        Assert.Equal("max", again.Assistant.AgentEffort);
        Assert.Contains("max", again.Assistant.EffortChoices);
        again.Assistant.SelectedAgent = 2;
        Assert.Equal("gpt-5", again.Assistant.AgentModel);
    }

    [Fact]
    public void Cursor_trusts_the_conversation_folder_it_runs_in_because_print_mode_cannot_ask()
    {
        var agent = new CursorCliAgent { WorkFolder = Folder("cursor"), LignumExe = "Lignum.exe", LignumArguments = ["--mcp"] };
        Directory.CreateDirectory(agent.WorkFolder);

        var arguments = agent.BuildArguments("hi").ToList();

        Assert.Contains("--trust", arguments);
        Assert.Equal(agent.WorkFolder, arguments[arguments.IndexOf("--workspace") + 1]);
    }

    [Fact]
    public void Cursor_parses_model_ids_from_agent_models_output()
    {
        var models = CursorCliAgent.ParseModelList("""
            Available models

            auto - Auto (default)
            composer-2.5 - Composer 2.5
            gpt-5.2-high - GPT-5.2 High

            Tip: use --model <id> to switch.
            """);

        Assert.Equal(["auto", "composer-2.5", "gpt-5.2-high"], models);
    }

    [Fact]
    public void A_custom_agent_fills_in_its_argument_template()
    {
        var agent = new CustomAgent("agy", "-p {prompt} --mcp-config \"{mcpConfig}\" --name \"two words\"")
            { WorkFolder = Folder("custom"), LignumExe = "Lignum.exe", LignumArguments = ["--mcp"] };
        Directory.CreateDirectory(agent.WorkFolder);

        var arguments = agent.BuildArguments("Find \"x\"");

        Assert.Equal("-p", arguments[0]);
        Assert.EndsWith("The user asks: Find 'x'", arguments[1]);
        Assert.Equal(Path.Combine(agent.WorkFolder, "mcp.json"), arguments[3]);
        Assert.Equal("two words", arguments[5]);
    }

    [Theory]
    [InlineData(0, "irm https://claude.ai/install.ps1 | iex")]
    [InlineData(1, "winget install GitHub.Copilot")]
    [InlineData(2, "irm 'https://cursor.com/install?win32=true' | iex")]
    [InlineData(3, "npm install -g @openai/codex")]
    public void Install_runs_the_official_installer_in_a_visible_PowerShell_window(int agent, string command)
    {
        var main = Start();
        var started = new List<System.Diagnostics.ProcessStartInfo>();
        main.Assistant.StartProcess = started.Add;
        main.Assistant.SelectedAgent = agent;

        main.Assistant.InstallAgentCommand.Execute(null);

        var start = Assert.Single(started);
        Assert.Equal("powershell.exe", start.FileName);
        Assert.True(start.UseShellExecute); // its own window, so the user sees what runs
        Assert.Contains("-NoExit", start.ArgumentList);
        Assert.Contains(command, start.ArgumentList.Last());
    }

    [Fact]
    public async Task Sign_in_opens_the_installed_agent_in_a_terminal()
    {
        var exe = FakeClaude();
        var main = Start();
        var started = new List<System.Diagnostics.ProcessStartInfo>();
        main.Assistant.StartProcess = started.Add;
        main.Assistant.AgentFactory = (_, folder) => new ClaudeCodeAgent
            { WorkFolder = folder, LignumExe = "", LignumArguments = [], Executable = exe };

        await main.Assistant.RefreshAgentCommand.ExecuteAsync(null);
        Assert.Equal(AgentState.Ready, main.Assistant.AgentState); // the fake answers `auth status` with exit code 0
        Assert.True(main.Assistant.ShowAgentIcons);
        Assert.False(main.Assistant.ShowAgentSetup);
        main.Assistant.SignInAgentCommand.Execute(null);

        Assert.Equal(exe, Assert.Single(started).FileName);
        Assert.Contains(main.Assistant.Entries, e => e.Text.StartsWith("Claude Code opens in a terminal"));
    }

    [Fact]
    public async Task Not_signed_in_shows_the_warning_and_a_login_error_brings_it_back()
    {
        var notSignedIn = Path.Combine(_dir, "claude-out.cmd");
        File.WriteAllLines(notSignedIn, ["@echo off", "more > nul", "exit /b 1"]); // `auth status` says: not signed in
        var main = Start();
        main.Assistant.AgentFactory = (_, folder) => new ClaudeCodeAgent
            { WorkFolder = folder, LignumExe = "", LignumArguments = [], Executable = notSignedIn };

        await main.Assistant.RefreshAgentCommand.ExecuteAsync(null);
        Assert.Equal(AgentState.NotSignedIn, main.Assistant.AgentState);
        Assert.True(main.Assistant.ShowAgentSetup);
        Assert.StartsWith("You are not signed in to Claude Code", main.Assistant.AgentStatus);

        var exe = FakeClaude("{\"type\":\"result\",\"subtype\":\"error\",\"is_error\":true,\"result\":\"Invalid API key - Please run /login\"}");
        main.Assistant.AgentFactory = (_, folder) => new ClaudeCodeAgent
            { WorkFolder = folder, LignumExe = "", LignumArguments = [], Executable = exe };
        await main.Assistant.RefreshAgentCommand.ExecuteAsync(null);
        Assert.Equal(AgentState.Ready, main.Assistant.AgentState);
        await Ask(main.Assistant, "hi");
        Assert.Equal(AgentState.NotSignedIn, main.Assistant.AgentState);
    }

    [Fact]
    public async Task An_error_from_the_agent_is_shown_and_a_missing_agent_says_how_to_install_it()
    {
        var exe = FakeClaude("""{"type":"result","subtype":"error_during_execution","is_error":true,"result":"Not logged in"}""");
        var main = Start();
        main.Assistant.AgentFactory = (_, folder) => new ClaudeCodeAgent
            { WorkFolder = folder, LignumExe = "", LignumArguments = [], Executable = exe };
        await Ask(main.Assistant, "hi");
        Assert.Equal("Claude Code: Not logged in",
            Assert.Single(main.Assistant.Entries, e => e.Kind == ChatEntryKind.Error).Text);

        main.Assistant.AgentFactory = (_, folder) => new ClaudeCodeAgent
            { WorkFolder = folder, LignumExe = "", LignumArguments = [], Executable = Path.Combine(_dir, "missing.exe") };
        main.Assistant.NewConversationCommand.Execute(null);
        await Ask(main.Assistant, "hi again");

        // A program that cannot be started: an error in the panel, not a crash.
        Assert.Single(main.Assistant.Entries, e => e.Kind == ChatEntryKind.Error);
        Assert.DoesNotContain(main.Assistant.Entries, e => e.Kind == ChatEntryKind.Assistant);
    }
}
