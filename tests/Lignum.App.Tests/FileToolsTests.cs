using System.Text;
using System.Text.Json;
using Lignum.App.Assistant;
using Lignum.App.Assistant.Permissions;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>AI file tools (ticket 18): list_dir, read_file, write_file, edit_file behind the permission store.</summary>
public sealed class FileToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public FileToolsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush();
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed class FakeApprover(GrantLifetime? lifetime) : IPermissionApprover
    {
        public List<PermissionRequest> Asked { get; } = [];

        public Task<ApprovalAnswer?> AskAsync(PermissionRequest request, IReadOnlyList<ScopeOption> options, CancellationToken cancellationToken)
        {
            Asked.Add(request);
            return Task.FromResult(lifetime is { } l ? new ApprovalAnswer(l, options[0]) : null);
        }
    }

    private async Task<(MainViewModel Main, FakeApprover Approver, string Log)> Start(GrantLifetime? answer = GrantLifetime.Once)
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        var log = Path.Combine(_dir, "app.log");
        File.WriteAllLines(log, ["start", "ERROR disk full", "done"]);
        await main.OpenFileAsync(log);
        main.Assistant.IsEnabled = true;
        var approver = new FakeApprover(answer);
        main.Assistant.Approver = approver;
        return (main, approver, log);
    }

    private static Task<ToolOutcome> Run(MainViewModel main, string tool, object arguments) =>
        main.Assistant.HandleToolCallAsync(
            new ToolCall(tool, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(arguments))!), default);

    private string Repo()
    {
        var repo = Path.Combine(_dir, "repo");
        Directory.CreateDirectory(Path.Combine(repo, "src"));
        File.WriteAllText(Path.Combine(repo, "src", "Disk.cs"), "class Disk\r\n{\r\n    int Free = 0;\r\n}\r\n");
        File.WriteAllText(Path.Combine(repo, "README.md"), "hello");
        return repo;
    }

    [Fact]
    public async Task List_and_read_ask_for_read_permission_and_show_folders_files_and_numbered_lines()
    {
        var (main, approver, _) = await Start();
        var repo = Repo();

        var list = await Run(main, "list_dir", new { path = repo });
        var read = await Run(main, "read_file", new { path = Path.Combine(repo, "src", "Disk.cs"), first_line = 2, count = 2 });

        Assert.Contains("src\\", list.Content);
        Assert.Contains("README.md  (5 bytes)", list.Content);
        Assert.Equal("2: {\r\n3:     int Free = 0;\r\n… more lines follow; read on from line 4.\r\n", read.Content.ReplaceLineEndings("\r\n"));
        Assert.Equal([(AiAccess.Read, true), (AiAccess.Read, false)], approver.Asked.Select(r => (r.Access, r.IsFolder)));
    }

    [Fact]
    public async Task A_denied_request_does_nothing_and_says_so()
    {
        var (main, _, _) = await Start(answer: null);
        var path = Path.Combine(_dir, "new.txt");

        var outcome = await Run(main, "write_file", new { path, content = "x" });

        Assert.True(outcome.IsError);
        Assert.Contains("did not allow", outcome.Content);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Write_creates_folders_and_a_utf8_file_and_shows_the_content_before_asking()
    {
        var (main, approver, _) = await Start();
        var path = Path.Combine(_dir, "out", "deep", "notes.md");

        var outcome = await Run(main, "write_file", new { path, content = "# Findings\nDisk full at 12:00" });

        Assert.False(outcome.IsError, outcome.Content);
        Assert.Equal("# Findings\nDisk full at 12:00", File.ReadAllText(path));
        Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble)); // no BOM on a new file
        Assert.Contains("New file", approver.Asked.Single().Detail);
        Assert.Contains("Disk full at 12:00", approver.Asked.Single().Detail);
    }

    [Fact]
    public async Task Edit_replaces_one_unique_occurrence_keeps_the_rest_and_the_bom()
    {
        var (main, approver, _) = await Start();
        var path = Path.Combine(_dir, "bom.cs");
        File.WriteAllText(path, "a = 1;\r\nb = 1;\r\n", new UTF8Encoding(true));

        var ambiguous = await Run(main, "edit_file", new { path, old_text = "= 1;", new_text = "= 2;" });
        var outcome = await Run(main, "edit_file", new { path, old_text = "b = 1;", new_text = "b = 2;" });

        Assert.True(ambiguous.IsError);
        Assert.Contains("2 times", ambiguous.Content);
        Assert.False(outcome.IsError, outcome.Content);
        Assert.Equal("a = 1;\r\nb = 2;\r\n", File.ReadAllText(path));
        Assert.True(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal("- b = 1;\r\n+ b = 2;\r\n", approver.Asked.Single().Detail!.ReplaceLineEndings("\r\n"));
    }

    [Fact]
    public async Task Replace_all_changes_every_occurrence()
    {
        var (main, _, _) = await Start();
        var path = Path.Combine(_dir, "x.txt");
        File.WriteAllText(path, "x x x");

        await Run(main, "edit_file", new { path, old_text = "x", new_text = "y", replace_all = true });

        Assert.Equal("y y y", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_log_open_in_a_tab_and_the_settings_folder_are_never_written_even_with_a_grant()
    {
        var (main, approver, log) = await Start();
        main.Assistant.Permissions.Add(AiAccess.Write, GrantScopeKind.Folder, _dir, GrantLifetime.Session);
        var before = File.ReadAllText(log);

        var write = await Run(main, "write_file", new { path = log, content = "gone" });
        var edit = await Run(main, "edit_file", new { path = log.ToUpperInvariant(), old_text = "ERROR", new_text = "OK" });
        var settings = await Run(main, "write_file", new { path = Path.Combine(_dir, "settings", "settings.json"), content = "{}" });

        Assert.True(write.IsError && edit.IsError && settings.IsError);
        Assert.Contains("never writes to a log file", write.Content);
        Assert.Equal(before, File.ReadAllText(log));
        Assert.Empty(approver.Asked);
    }

    [Fact]
    public async Task A_session_grant_stops_the_questions_for_that_folder()
    {
        var (main, approver, _) = await Start(GrantLifetime.Session);
        var repo = Repo();

        await Run(main, "list_dir", new { path = repo });
        await Run(main, "list_dir", new { path = repo });

        Assert.Single(approver.Asked);
    }

    [Fact]
    public async Task Without_an_approver_only_granted_paths_can_be_read()
    {
        var (main, _, _) = await Start();
        main.Assistant.Approver = null;
        var repo = Repo();

        Assert.True((await Run(main, "list_dir", new { path = repo })).IsError);
        main.Assistant.Permissions.Add(AiAccess.Read, GrantScopeKind.Folder, repo, GrantLifetime.Session);
        Assert.False((await Run(main, "list_dir", new { path = repo })).IsError);
    }
}
