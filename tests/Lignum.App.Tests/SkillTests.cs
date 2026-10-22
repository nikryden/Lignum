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
        Assert.Equal(["errors", "timeline"], assistant.SkillSuggestions.Select(s => s.Name));

        assistant.Input = "/ti";
        Assert.Equal("timeline", Assert.Single(assistant.SkillSuggestions).Name);

        assistant.AcceptSkillSuggestion();
        Assert.Equal("/timeline ", assistant.Input);
        Assert.False(assistant.IsSkillListOpen);

        assistant.Input = "/nothing-like-this";
        Assert.Empty(assistant.SkillSuggestions);
        Assert.Equal("No skill matches /nothing-like-this.", assistant.SkillListHint);
    }

    [Fact]
    public async Task With_no_skills_a_slash_says_where_skills_are_looked_for()
    {
        var assistant = (await Start()).Assistant;

        assistant.Input = "/";

        Assert.True(assistant.IsSkillListOpen);
        Assert.Empty(assistant.SkillSuggestions);
        Assert.Contains(GlobalFolder, assistant.SkillListHint);
        Assert.Contains("this log's folder only", assistant.SkillListHint);
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
        var skill = main.Assistant.Tools.LoadSkills()[0];
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
        var skills = main.Assistant.Tools.LoadSkills();
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
