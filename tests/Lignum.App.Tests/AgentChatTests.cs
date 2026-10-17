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
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
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
