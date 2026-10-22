using System.Text.Json;
using Lignum.App.Assistant;
using Lignum.App.Assistant.Permissions;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>run_command (ticket 19): commands run only with an Execute grant, with a timeout and captured output.</summary>
public sealed class RunCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public RunCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush();
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed class FakeApprover(GrantLifetime? lifetime, int scopeIndex = 0) : IPermissionApprover
    {
        public List<(PermissionRequest Request, IReadOnlyList<ScopeOption> Options)> Asked { get; } = [];

        public Task<ApprovalAnswer?> AskAsync(PermissionRequest request, IReadOnlyList<ScopeOption> options, CancellationToken cancellationToken)
        {
            Asked.Add((request, options));
            return Task.FromResult(lifetime is { } l ? new ApprovalAnswer(l, options[scopeIndex]) : null);
        }
    }

    private (MainViewModel Main, FakeApprover Approver) Start(GrantLifetime? answer = GrantLifetime.Once, int scopeIndex = 0)
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        main.Assistant.IsEnabled = true;
        var approver = new FakeApprover(answer, scopeIndex);
        main.Assistant.Approver = approver;
        return (main, approver);
    }

    private Task<ToolOutcome> Run(MainViewModel main, object arguments, CancellationToken cancellationToken = default) =>
        main.Assistant.HandleToolCallAsync(
            new ToolCall("run_command", JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(arguments))!),
            cancellationToken);

    [Fact]
    public async Task A_cmd_script_runs_in_the_given_folder_and_returns_its_output_and_exit_code()
    {
        var (main, approver) = Start();

        var outcome = await Run(main, new { shell = "cmd", command = "cd & echo hello", cwd = _dir });

        Assert.False(outcome.IsError, outcome.Content);
        Assert.Contains(_dir, outcome.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hello", outcome.Content);
        Assert.EndsWith("Exit code 0.", outcome.Content);
        var request = approver.Asked.Single().Request;
        Assert.Equal((AiAccess.Execute, "cmd", "cmd: cd & echo hello"), (request.Access, request.Target, request.CommandLine));
        Assert.Contains(_dir, request.Detail);
    }

    [Fact]
    public async Task A_failing_command_is_an_error_with_its_exit_code()
    {
        var (main, _) = Start();

        var outcome = await Run(main, new { shell = "cmd", command = "exit /b 3", cwd = _dir });

        Assert.True(outcome.IsError);
        Assert.EndsWith("Exit code 3.", outcome.Content);
    }

    [Fact]
    public async Task A_denied_command_does_not_run()
    {
        var (main, _) = Start(answer: null);
        var marker = Path.Combine(_dir, "ran.txt");

        var outcome = await Run(main, new { shell = "cmd", command = $"echo x > \"{marker}\"", cwd = _dir });

        Assert.True(outcome.IsError);
        Assert.Contains("did not allow", outcome.Content);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task A_program_grant_covers_other_commands_of_it_and_an_exact_grant_does_not()
    {
        var (main, approver) = Start(GrantLifetime.Session, scopeIndex: 0); // "only this exact command"
        await Run(main, new { shell = "cmd", command = "echo one", cwd = _dir });
        await Run(main, new { shell = "cmd", command = "echo one", cwd = _dir });
        await Run(main, new { shell = "cmd", command = "echo two", cwd = _dir });
        Assert.Equal(2, approver.Asked.Count);

        main.Assistant.Permissions.Add(AiAccess.Execute, GrantScopeKind.Executable, "cmd", GrantLifetime.Session);
        await Run(main, new { shell = "cmd", command = "echo three", cwd = _dir });
        Assert.Equal(2, approver.Asked.Count);
    }

    [Fact]
    public async Task A_command_that_runs_too_long_is_stopped_at_the_timeout()
    {
        var (main, _) = Start();

        var outcome = await Run(main, new { shell = "cmd", command = "ping -n 30 127.0.0.1 >nul", cwd = _dir, timeout_seconds = 1 });

        Assert.True(outcome.IsError);
        Assert.Contains("timeout", outcome.Content);
    }

    [Fact]
    public async Task Stop_in_the_chat_cancels_a_running_command()
    {
        var (main, _) = Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Run(main, new { shell = "cmd", command = "ping -n 30 127.0.0.1 >nul", cwd = _dir }, stop.Token));
    }

    [Theory]
    [InlineData("none", "git log -n 5", "git", false)]
    [InlineData("", "python \"my script.py\" --x", "python", false)]
    [InlineData("pwsh", "Get-ChildItem", "pwsh", true)]
    [InlineData("wsl", "ls -la", "wsl", true)]
    public void Commands_are_planned_with_the_program_a_grant_is_checked_against(string shell, string command, string grant, bool script)
    {
        var plan = AssistantTools.PlanCommand(shell, command);

        Assert.Equal((grant, script), (plan.GrantName, plan.IsScript));
        if (!script) Assert.Equal(CustomAgent.SplitArguments(command).Skip(1), plan.ArgumentList!);
    }

    [Fact]
    public void An_unknown_shell_is_rejected() =>
        Assert.Throws<ToolInputException>(() => AssistantTools.PlanCommand("bash", "ls"));
}
