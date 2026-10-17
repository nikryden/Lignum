using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using Lignum.App.Assistant;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>The built-in assistant (ticket 05), with a scripted Claude: the tool loop, the tools and privacy.</summary>
public sealed class AssistantTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];
    private readonly FakeClaude _claude = new();

    public AssistantTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<MainViewModel> Open()
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName))
        {
            ClaudeChatFactory = _ => _claude,
        });
        _owned.Add(main);
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllLines(path, ["start", "ERROR disk full", "ok", "ERROR timeout", "done"]);
        await main.OpenFileAsync(path);
        main.Assistant.IsEnabled = true;
        return main;
    }

    private static async Task Ask(AssistantViewModel assistant, string question)
    {
        assistant.Input = question;
        await assistant.SendCommand.ExecuteAsync(null);
    }

    private static Dictionary<string, JsonElement> Json(object value) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value))!;

    [Fact]
    public async Task Nothing_is_sent_until_the_assistant_is_turned_on()
    {
        var main = await Open();
        main.Assistant.IsEnabled = false;
        main.Assistant.Input = "hello";

        Assert.False(main.Assistant.SendCommand.CanExecute(null));
        Assert.Empty(_claude.Requests);
    }

    [Fact]
    public async Task Tool_calls_run_and_their_results_go_back_until_Claude_answers()
    {
        var main = await Open();
        _claude.Reply(FakeClaude.Tools(("t1", "search", Json(new { pattern = "ERROR" }))));
        _claude.Reply(FakeClaude.Text("Two errors: lines 2 and 4."));
        var ran = new List<(ToolCall Call, ToolOutcome Outcome)>();
        main.Assistant.ToolRan += (call, outcome) => ran.Add((call, outcome));

        await Ask(main.Assistant, "Any errors?");

        Assert.Equal(2, _claude.Requests.Count);
        var (call, outcome) = Assert.Single(ran);
        Assert.Equal("search", call.Name);
        Assert.Contains("2 matching lines", outcome.Content);
        Assert.Contains("2: ERROR disk full", outcome.Content);
        Assert.Equal(
            ["User: Any errors?", "Activity: Searching for \"ERROR\"", "Activity: Claude read 2 lines from app.log",
             "Assistant: Two errors: lines 2 and 4."],
            main.Assistant.Entries.Select(e => $"{e.Kind}: {e.Text}"));
        Assert.Equal(3, _claude.Requests[1].Messages.Count); // the question, the tool call and its result
        Assert.False(main.Assistant.IsBusy);
    }

    [Fact]
    public async Task Tools_change_the_tab_and_bad_input_comes_back_as_an_error_result()
    {
        var main = await Open();
        _claude.Reply(FakeClaude.Tools(
            ("t1", "add_filter_rule", Json(new { text = "ERROR", kind = "include" })),
            ("t2", "add_bookmark", Json(new { line = 4, note = "timeout here" })),
            ("t3", "go_to_line", Json(new { line = 99 }))));
        _claude.Reply(FakeClaude.Text("Done."));
        var outcomes = new List<ToolOutcome>();
        main.Assistant.ToolRan += (_, outcome) => outcomes.Add(outcome);

        await Ask(main.Assistant, "Show me the errors");

        var tab = main.SelectedTab!;
        Assert.Equal("ERROR", Assert.Single(tab.Search.FilterRules).Text);
        Assert.Equal("timeout here", tab.Bookmarks.NoteFor(3));
        Assert.False(outcomes[0].IsError);
        Assert.True(outcomes[2].IsError); // line 99 does not exist: an error result, not an exception
        Assert.Contains("outside the file", outcomes[2].Content);
    }

    [Fact]
    public async Task Reading_lines_is_capped_and_reported()
    {
        var main = await Open();
        _claude.Reply(FakeClaude.Tools(("t1", "read_lines", Json(new { first_line = 2, count = 500 }))));
        _claude.Reply(FakeClaude.Text("ok"));
        ToolOutcome? read = null;
        main.Assistant.ToolRan += (_, outcome) => read = outcome;

        await Ask(main.Assistant, "read");

        Assert.Equal("2: ERROR disk full\r\n3: ok\r\n4: ERROR timeout\r\n5: done\r\n", read!.Content.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.Contains(main.Assistant.Entries, e => e.Text == "Claude read 4 lines from app.log");
    }

    [Fact]
    public async Task Stop_drops_the_unfinished_exchange_from_the_conversation()
    {
        var main = await Open();
        _claude.Reply(FakeClaude.Text("first answer"));
        await Ask(main.Assistant, "first");
        _claude.Hang = true;

        main.Assistant.Input = "second";
        var sending = main.Assistant.SendCommand.ExecuteAsync(null);
        main.Assistant.StopCommand.Execute(null);
        await sending;

        _claude.Hang = false;
        _claude.Reply(FakeClaude.Text("third answer"));
        await Ask(main.Assistant, "third");
        Assert.Equal(3, _claude.Requests[^1].Messages.Count); // first, its answer, third: "second" is gone
        Assert.Contains(main.Assistant.Entries, e => e.Text.StartsWith("Stopped."));
    }

    [Fact]
    public async Task The_api_key_is_saved_encrypted_and_read_back()
    {
        var main = await Open();
        main.Assistant.SetApiKey("sk-ant-test-123");

        Assert.True(main.Assistant.HasApiKey);
        Assert.Equal("sk-ant-test-123", main.Assistant.ApiKey());
        SettingsStore.Flush();
        Assert.DoesNotContain("sk-ant-test-123", File.ReadAllText(Path.Combine(_dir, "settings", AppSettings.FileName)));
    }

    /// <summary>A scripted Claude: returns the queued replies in order and records each request.</summary>
    private sealed class FakeClaude : IClaudeChat
    {
        private readonly Queue<AssistantReply> _replies = new();

        public List<MessageCreateParams> Requests { get; } = [];

        public bool Hang { get; set; }

        public void Reply(AssistantReply reply) => _replies.Enqueue(reply);

        public async Task<AssistantReply> SendAsync(MessageCreateParams request, Action<string> onText,
                                                    CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            return _replies.Dequeue();
        }

        public static AssistantReply Text(string text) =>
            new(text, [], "end_turn", [new BetaTextBlockParam { Text = text }]);

        public static AssistantReply Tools(params (string Id, string Name, Dictionary<string, JsonElement> Input)[] calls) =>
            new("", [.. calls.Select(c => new ToolCall(c.Id, c.Name, c.Input))], "tool_use",
                [.. calls.Select(c => (BetaContentBlockParam)new BetaToolUseBlockParam { ID = c.Id, Name = c.Name, Input = c.Input })]);
    }
}
