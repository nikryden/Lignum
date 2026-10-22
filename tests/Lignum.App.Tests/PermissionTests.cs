using System.Diagnostics;
using Lignum.App.Assistant.Permissions;
using Lignum.App.Settings;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>AI permissions (ticket 17, ADR 0002): grants, path matching, lifetimes and revoking.</summary>
public sealed class PermissionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public PermissionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SettingsStore.Flush();
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private SettingsFile<AppSettings> Settings() =>
        new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName);

    private string Repo(string name = "repo")
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(path, "src"));
        File.WriteAllText(Path.Combine(path, "src", "a.cs"), "x");
        return path;
    }

    /// <summary>Answers every question the same way and records them.</summary>
    private sealed class FakeApprover(GrantLifetime? lifetime, int scopeIndex = 0) : IPermissionApprover
    {
        public List<(PermissionRequest Request, IReadOnlyList<ScopeOption> Options)> Asked { get; } = [];

        public Task<ApprovalAnswer?> AskAsync(PermissionRequest request, IReadOnlyList<ScopeOption> options, CancellationToken cancellationToken)
        {
            Asked.Add((request, options));
            return Task.FromResult(lifetime is { } l ? new ApprovalAnswer(l, options[scopeIndex]) : null);
        }
    }

    [Fact]
    public void A_folder_grant_covers_its_subfolders_but_not_a_sibling_with_the_same_prefix()
    {
        var store = new PermissionStore(null);
        var repo = Repo();
        store.Add(AiAccess.Read, GrantScopeKind.Folder, repo, GrantLifetime.Session);

        Assert.True(store.IsAllowed(new PermissionRequest(AiAccess.Read, Path.Combine(repo, "src", "a.cs"))));
        Assert.True(store.IsAllowed(new PermissionRequest(AiAccess.Read, repo.ToUpperInvariant() + "\\", IsFolder: true)));
        Assert.True(store.IsAllowed(new PermissionRequest(AiAccess.Read, Path.Combine(repo, "src", "new", "b.cs"))));
        Assert.False(store.IsAllowed(new PermissionRequest(AiAccess.Read, Repo("repo2"), IsFolder: true)));
        Assert.False(store.IsAllowed(new PermissionRequest(AiAccess.Read, Path.Combine(repo, "..", "repo2", "src", "a.cs"))));
    }

    [Fact]
    public void Write_implies_read_but_read_does_not_allow_write_and_neither_allows_execute()
    {
        var store = new PermissionStore(null);
        var file = Path.Combine(Repo(), "src", "a.cs");
        store.Add(AiAccess.Read, GrantScopeKind.File, file, GrantLifetime.Session);
        Assert.False(store.IsAllowed(new PermissionRequest(AiAccess.Write, file)));

        store.Add(AiAccess.Write, GrantScopeKind.File, file, GrantLifetime.Session);
        Assert.True(store.IsAllowed(new PermissionRequest(AiAccess.Write, file)));
        Assert.True(store.IsAllowed(new PermissionRequest(AiAccess.Read, file)));
        Assert.False(store.IsAllowed(new PermissionRequest(AiAccess.Execute, file, CommandLine: file)));
    }

    [Fact]
    public void A_program_grant_covers_any_command_of_it_and_a_command_grant_only_that_exact_command()
    {
        var store = new PermissionStore(null);
        store.Add(AiAccess.Execute, GrantScopeKind.Command, "git status", GrantLifetime.Session);
        Assert.True(store.IsAllowed(new PermissionRequest(AiAccess.Execute, "git", CommandLine: "git status")));
        Assert.False(store.IsAllowed(new PermissionRequest(AiAccess.Execute, "git", CommandLine: "git push --force")));

        store.Add(AiAccess.Execute, GrantScopeKind.Executable, "git", GrantLifetime.Session);
        Assert.True(store.IsAllowed(new PermissionRequest(AiAccess.Execute, "GIT", CommandLine: "git push --force")));
        Assert.False(store.IsAllowed(new PermissionRequest(AiAccess.Execute, "pwsh", CommandLine: "pwsh -c ls")));
    }

    [Fact]
    public async Task Once_asks_every_time_session_and_always_ask_once()
    {
        var file = new PermissionRequest(AiAccess.Read, Path.Combine(Repo(), "src", "a.cs"));
        foreach (var (lifetime, expectedQuestions) in new[] { (GrantLifetime.Once, 2), (GrantLifetime.Session, 1), (GrantLifetime.Always, 1) })
        {
            var store = new PermissionStore(null);
            var approver = new FakeApprover(lifetime);

            Assert.True(await store.RequestAsync(file, approver, default));
            Assert.True(await store.RequestAsync(file, approver, default));

            Assert.Equal(expectedQuestions, approver.Asked.Count);
        }
    }

    [Fact]
    public async Task Deny_returns_false_and_stores_nothing()
    {
        var store = new PermissionStore(null);

        Assert.False(await store.RequestAsync(new PermissionRequest(AiAccess.Write, Path.Combine(_dir, "x.txt")), new FakeApprover(null), default));

        Assert.Empty(store.SessionGrants);
        Assert.Empty(store.AlwaysGrants);
    }

    [Fact]
    public async Task Always_grants_are_saved_and_session_grants_are_not()
    {
        var repo = Repo();
        var first = new PermissionStore(Settings());
        await first.RequestAsync(new PermissionRequest(AiAccess.Write, Path.Combine(repo, "src", "a.cs")), new FakeApprover(GrantLifetime.Always, scopeIndex: 1), default);
        await first.RequestAsync(new PermissionRequest(AiAccess.Execute, "git", CommandLine: "git log"), new FakeApprover(GrantLifetime.Session), default);
        SettingsStore.Flush();

        var restarted = new PermissionStore(Settings());

        var grant = Assert.Single(restarted.AlwaysGrants);
        Assert.Equal((AiAccess.Write, GrantScopeKind.Folder), (grant.Access, grant.Kind));
        Assert.True(CanonicalPath.IsUnder(CanonicalPath.Of(Path.Combine(repo, "src", "a.cs")), grant.Scope));
        Assert.Empty(restarted.SessionGrants);
        Assert.False(restarted.IsAllowed(new PermissionRequest(AiAccess.Execute, "git", CommandLine: "git log")));
    }

    [Fact]
    public void Revoking_an_always_grant_removes_it_from_the_settings_and_revoke_all_clears_everything()
    {
        var repo = Repo();
        var store = new PermissionStore(Settings());
        store.Add(AiAccess.Read, GrantScopeKind.Folder, repo, GrantLifetime.Always);
        store.Add(AiAccess.Execute, GrantScopeKind.Executable, "git", GrantLifetime.Always);
        store.Add(AiAccess.Execute, GrantScopeKind.Executable, "pwsh", GrantLifetime.Session);

        store.Revoke(store.AlwaysGrants[0]);
        SettingsStore.Flush();
        Assert.Equal(["git"], new PermissionStore(Settings()).AlwaysGrants.Select(g => g.Scope));

        store.RevokeAll();
        SettingsStore.Flush();
        Assert.Empty(store.SessionGrants);
        Assert.Empty(new PermissionStore(Settings()).AlwaysGrants);
    }

    [Fact]
    public void The_dialog_offers_the_file_then_its_folder_and_the_exact_command_then_the_program()
    {
        var file = Path.Combine(Repo(), "src", "a.cs");

        var paths = PermissionStore.ScopeOptions(new PermissionRequest(AiAccess.Write, file));
        var commands = PermissionStore.ScopeOptions(new PermissionRequest(AiAccess.Execute, "git", CommandLine: "git status"));

        Assert.Equal([GrantScopeKind.File, GrantScopeKind.Folder], paths.Select(o => o.Kind));
        Assert.Equal(Path.GetDirectoryName(paths[0].Scope), paths[1].Scope);
        Assert.Equal([("git status", GrantScopeKind.Command), ("git", GrantScopeKind.Executable)], commands.Select(o => (o.Scope, o.Kind)));
    }

    [Fact]
    public void A_junction_is_resolved_so_a_grant_on_its_target_covers_it_and_a_grant_elsewhere_does_not()
    {
        var target = Repo("target");
        var junction = Path.Combine(_dir, "link");
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"")
                   { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!)
        {
            mklink.WaitForExit();
            Assert.Equal(0, mklink.ExitCode);
        }

        Assert.Equal(CanonicalPath.Of(Path.Combine(target, "src", "a.cs")), CanonicalPath.Of(Path.Combine(junction, "src", "a.cs")));

        var store = new PermissionStore(null);
        store.Add(AiAccess.Read, GrantScopeKind.Folder, Repo("elsewhere"), GrantLifetime.Session);
        Assert.False(store.IsAllowed(new PermissionRequest(AiAccess.Read, Path.Combine(junction, "src", "a.cs"))));
        store.Add(AiAccess.Read, GrantScopeKind.Folder, target, GrantLifetime.Session);
        Assert.True(store.IsAllowed(new PermissionRequest(AiAccess.Read, Path.Combine(junction, "src", "a.cs"))));
    }

    [Theory]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\a\..\b\", @"C:\b")]
    public void Paths_are_full_without_a_trailing_separator_except_a_root(string input, string expected) =>
        Assert.Equal(expected, CanonicalPath.Of(input), ignoreCase: true);
}
