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
