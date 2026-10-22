using System.Text.Json;
using Lignum.App.Assistant;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Skills: saved instructions used with /name in the chat, global or next to the log, for every agent.</summary>
public sealed class SkillTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public SkillTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string GlobalFolder => Path.Combine(_dir, "global-skills");

    private string LogPath => Path.Combine(_dir, "logs", "app.log");

    private string LocalFolder => Path.Combine(_dir, "logs", ".lignum", "skills");

    /// <summary>Stands in for ~/.claude/skills, so the tests never see the real user's skills.</summary>
    private string ClaudeSkillsFolder => Path.Combine(_dir, "claude-skills");

    /// <summary>A stand-in for claude.cmd that saves what it was asked (stdin) and answers in stream-json.</summary>
    private string FakeClaude()
    {
        var path = Path.Combine(_dir, "claude.cmd");
        File.WriteAllLines(path,
        [
            "@echo off",
            "more > \"%~dp0stdin.txt\"",
            """echo {"type":"result","subtype":"success","is_error":false,"result":"done"}""",
        ]);
        return path;
    }

    private static void WriteSkill(string folder, string directory, string name, string description, string body)
    {
        Directory.CreateDirectory(Path.Combine(folder, directory));
        File.WriteAllText(Path.Combine(folder, directory, SkillLibrary.FileName),
            $"---\nname: {name}\ndescription: {description}\n---\n\n{body}\n");
    }

    private async Task<MainViewModel> Start()
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllLines(LogPath, ["start", "ERROR disk full"]);
        await main.OpenFileAsync(LogPath);
        main.Assistant.Tools.GlobalSkillsFolder = GlobalFolder;
        main.Assistant.AgentFactory = (_, folder) => new ClaudeCodeAgent
            { WorkFolder = folder, LignumExe = "", LignumArguments = [], Executable = FakeClaude(), SkillsFolder = ClaudeSkillsFolder };
        main.Assistant.IsEnabled = true;
        return main;
    }

    private static ToolCall Call(string tool, object arguments) =>
        new(tool, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(arguments))!);

    [Fact]
    public void Global_and_local_skills_load_and_a_local_one_hides_a_global_one_of_the_same_name()
    {
        WriteSkill(GlobalFolder, "a", "Errors", "global errors", "global body");
        WriteSkill(GlobalFolder, "b", "timeline", "build a timeline", "timeline body");
        WriteSkill(LocalFolder, "c", "errors", "local errors", "local body");

        var skills = SkillLibrary.Load(GlobalFolder, SkillLibrary.LocalFolderFor(LogPath));

        Assert.Equal(["errors", "timeline"], skills.Select(s => s.Name));
        Assert.Equal(SkillScope.Local, skills[0].Scope);
        Assert.Equal("local errors", skills[0].Description);
        Assert.Equal("local body", skills[0].ReadInstructions());
    }

    [Fact]
    public void A_skill_without_a_name_uses_its_folder_name_and_missing_folders_give_no_skills()
    {
        Directory.CreateDirectory(Path.Combine(GlobalFolder, "Find Slow Calls"));
        File.WriteAllText(Path.Combine(GlobalFolder, "Find Slow Calls", SkillLibrary.FileName), "Just instructions.");

        Assert.Equal("find-slow-calls", Assert.Single(SkillLibrary.Load(GlobalFolder, null)).Name);
        Assert.Empty(SkillLibrary.Load(Path.Combine(_dir, "missing"), Path.Combine(_dir, "missing2")));
    }

    [Theory]
    [InlineData("/errors", "errors", "")]
    [InlineData("/Errors in the last hour", "errors", "in the last hour")]
    [InlineData("/errors\nsecond line", "errors", "second line")]
    public void A_slash_command_is_split_into_name_and_rest(string message, string name, string rest)
    {
        Assert.Equal((name, rest), SkillLibrary.ParseCommand(message));
    }

    [Theory]
    [InlineData("what is /errors")]
    [InlineData("/var/log is full")]
    public void Other_messages_are_not_commands(string message)
    {
        Assert.Null(SkillLibrary.ParseCommand(message));
    }

    [Fact]
    public async Task Typing_a_slash_suggests_matching_skills_and_accepting_one_fills_the_input()
    {
        WriteSkill(GlobalFolder, "a", "errors", "find errors", "x");
        WriteSkill(LocalFolder, "b", "timeline", "build a timeline", "y");
        var assistant = (await Start()).Assistant;

        assistant.Input = "/";
        Assert.True(assistant.IsSkillListOpen);
        Assert.Equal(["errors", "timeline"], assistant.SkillSuggestions.Where(s => s.Scope is SkillScope.Global or SkillScope.Local).Select(s => s.Name));

        assistant.Input = "/timel";
        Assert.Equal("timeline", Assert.Single(assistant.SkillSuggestions).Name);

        assistant.AcceptSkillSuggestion();
        Assert.Equal("/timeline ", assistant.Input);
        Assert.False(assistant.IsSkillListOpen);

        assistant.Input = "/nothing-like-this";
        Assert.Empty(assistant.SkillSuggestions);
        Assert.Equal("No command or skill matches /nothing-like-this.", assistant.SkillListHint);
    }

    [Fact]
    public async Task With_no_skills_a_slash_lists_the_commands_and_says_where_skills_are_looked_for()
    {
        var assistant = (await Start()).Assistant;

        assistant.Input = "/";

        Assert.True(assistant.IsSkillListOpen);
        Assert.Contains("clear", assistant.SkillSuggestions.Select(s => s.Name));
        Assert.Contains(GlobalFolder, assistant.SkillListHint);
        Assert.Contains("this log's folder only", assistant.SkillListHint);
    }

    [Fact]
    public void Lignum_ships_built_in_skills_and_a_users_skill_of_the_same_name_replaces_one()
    {
        Assert.Equal(["compare-runs", "explain-exception", "timeline", "trace-to-source", "triage-errors"],
            SkillLibrary.BuiltIn.Select(s => s.Name));
        Assert.All(SkillLibrary.BuiltIn, s => Assert.NotEmpty(s.Description));
        Assert.Contains("search", SkillLibrary.BuiltIn.Single(s => s.Name == "triage-errors").ReadInstructions());

        WriteSkill(GlobalFolder, "t", "timeline", "my own timeline", "mine");
        var skills = SkillLibrary.Load(GlobalFolder, null, builtIn: true);

        Assert.Equal(SkillScope.Global, skills.Single(s => s.Name == "timeline").Scope);
        Assert.Equal("built-in", skills.Single(s => s.Name == "triage-errors").ScopeText);
        Assert.DoesNotContain(SkillLibrary.Load(GlobalFolder, null), s => s.Scope == SkillScope.BuiltIn);
    }

    [Fact]
    public async Task The_agent_can_fetch_a_built_in_skill_with_get_skill()
    {
        var main = await Start();

        var outcome = await main.Assistant.HandleToolCallAsync(Call("get_skill", new { name = "explain-exception" }), default);

        Assert.False(outcome.IsError, outcome.Content);
        Assert.Contains("(built-in)", outcome.Content);
        Assert.Contains("stack trace", outcome.Content);
    }

    [Fact]
    public async Task Claude_Codes_own_skills_are_listed_and_sent_as_typed_for_Claude_to_expand()
    {
        WriteSkill(Path.Combine(ClaudeSkillsFolder, "synced", "abc"), "pdf", "pdf", "work with PDF files", "x");
        WriteSkill(ClaudeSkillsFolder, "t", "timeline", "Claude's own timeline", "y"); // hidden: Lignum has one
        var assistant = (await Start()).Assistant;

        assistant.Input = "/pd";
        var pdf = Assert.Single(assistant.SkillSuggestions);
        assistant.Input = "/timeline";
        var timeline = assistant.SkillSuggestions.Single(s => s.Name == "timeline");
        assistant.Input = "/pdf summarize report.pdf";
        await assistant.SendCommand.ExecuteAsync(null);

        Assert.Equal(("Claude Code", SkillScope.Agent), (pdf.ScopeText, pdf.Scope));
        Assert.Equal(SkillScope.BuiltIn, timeline.Scope);
        Assert.Equal("/pdf summarize report.pdf", File.ReadAllText(Path.Combine(_dir, "stdin.txt")).Trim());
        Assert.Contains(assistant.Entries, e => e.Kind == ChatEntryKind.Assistant && e.Text == "done");
    }

    [Fact]
    public async Task Chat_commands_run_in_Lignum_and_are_never_sent_to_the_agent()
    {
        WriteSkill(GlobalFolder, "a", "clear", "a skill named like a command", "x");
        var assistant = (await Start()).Assistant;
        var permissions = 0;
        assistant.PermissionsRequested += (_, _) => permissions++;
        assistant.Entries.Add(new ChatEntry(ChatEntryKind.User, "old question"));

        foreach (var command in new[] { "/model opus", "/effort high", "/effort turbo", "/permissions", "/help", "/model" })
        {
            assistant.Input = command;
            await assistant.SendCommand.ExecuteAsync(null);
            Assert.Equal("", assistant.Input);
        }

        Assert.Equal("opus", assistant.AgentModel);
        Assert.Equal("high", assistant.AgentEffort);
        Assert.Equal(1, permissions);
        var texts = assistant.Entries.Select(e => e.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("Reasoning levels for Claude Code:"));
        Assert.Contains(texts, t => t.Contains("/clear (command)") && !t.Contains("a skill named like a command"));
        Assert.Equal("Model: opus", texts[^1]);

        assistant.Input = "/clear";
        await assistant.SendCommand.ExecuteAsync(null);
        Assert.Empty(assistant.Entries);
        Assert.False(assistant.IsBusy);
    }

    [Fact]
    public async Task The_agent_is_told_to_fetch_the_skill_and_get_skill_returns_its_instructions()
    {
        WriteSkill(LocalFolder, "a", "errors", "find errors", "Search for ERROR and bookmark each in red.");
        var main = await Start();

        var tool = await main.Assistant.HandleToolCallAsync(Call("get_skill", new { name = "/errors" }), default);
        var list = await main.Assistant.HandleToolCallAsync(Call("list_skills", new { }), default);
        var missing = await main.Assistant.HandleToolCallAsync(Call("get_skill", new { name = "nope" }), default);

        Assert.Contains("Search for ERROR and bookmark each in red.", tool.Content);
        Assert.Contains("errors (local): find errors", list.Content);
        Assert.True(missing.IsError);
        var skill = main.Assistant.Tools.LoadSkills().Single(s => s.Name == "errors");
        var prompt = SkillLibrary.PromptFor(skill, "only today");
        Assert.Contains("get_skill", prompt);
        Assert.Contains("\"errors\"", prompt);
        Assert.Contains("only today", prompt);
        Assert.DoesNotContain("\n", prompt); // fits agents that take the prompt on one command line
    }

    [Fact]
    public async Task The_AI_saves_skills_locally_or_globally_and_asks_before_replacing_one()
    {
        var main = await Start();

        var local = await main.Assistant.HandleToolCallAsync(Call("save_skill",
            new { name = "First Error", description = "find the\nfirst error", instructions = "Search for ERROR; go to the first." }), default);
        var global = await main.Assistant.HandleToolCallAsync(Call("save_skill",
            new { name = "timeline", instructions = "Build a timeline.", scope = "global" }), default);
        var again = await main.Assistant.HandleToolCallAsync(Call("save_skill",
            new { name = "first-error", instructions = "changed" }), default);

        Assert.False(local.IsError, local.Content);
        Assert.False(global.IsError, global.Content);
        Assert.True(File.Exists(Path.Combine(LocalFolder, "first-error", SkillLibrary.FileName)));
        Assert.True(File.Exists(Path.Combine(GlobalFolder, "timeline", SkillLibrary.FileName)));
        Assert.True(again.IsError);
        Assert.Contains("already exists", again.Content);

        var replaced = await main.Assistant.HandleToolCallAsync(Call("save_skill",
            new { name = "first-error", instructions = "changed", overwrite = true }), default);
        Assert.False(replaced.IsError);
        var skills = main.Assistant.Tools.LoadSkills().Where(s => s.Scope != SkillScope.BuiltIn).ToList();
        Assert.Equal(["first-error", "timeline"], skills.Select(s => s.Name));
        Assert.Equal("changed", skills[0].ReadInstructions());
        Assert.Equal(SkillScope.Local, skills[0].Scope);
    }

    [Fact]
    public async Task A_skill_name_cannot_leave_the_skills_folder()
    {
        var main = await Start();

        await main.Assistant.HandleToolCallAsync(Call("save_skill",
            new { name = @"..\..\evil", instructions = "x", scope = "global" }), default);

        Assert.True(File.Exists(Path.Combine(GlobalFolder, "evil", SkillLibrary.FileName)));
        Assert.False(File.Exists(Path.Combine(_dir, "evil", SkillLibrary.FileName)));
    }

    [Fact]
    public async Task Sending_an_unknown_skill_shows_an_error_and_keeps_the_input()
    {
        var assistant = (await Start()).Assistant;
        assistant.Input = "/nope do it";

        await assistant.SendCommand.ExecuteAsync(null);

        Assert.Equal("/nope do it", assistant.Input);
        Assert.Contains(assistant.Entries, e => e.Kind == ChatEntryKind.Error && e.Text.Contains("no skill /nope"));
    }
}
