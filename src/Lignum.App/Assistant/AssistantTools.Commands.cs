using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lignum.App.Assistant.Permissions;

namespace Lignum.App.Assistant;

// run_command (ticket 19, ADR 0002): the AI runs a command (git, PowerShell, cmd, Python, WSL, ...) only with an
// Execute grant on the program or on that exact command, or when the user allows it now. Runs in the background
// with no console and no input, a timeout and an output cap; Stop in the chat cancels it and kills its process tree.
public sealed partial class AssistantTools
{
    internal const int MaxCommandOutputChars = 100_000;
    internal const int DefaultCommandTimeoutSeconds = 120;
    internal const int MaxCommandTimeoutSeconds = 600;

    /// <summary>How a command is started: the program, its arguments, and the name a grant is checked against.</summary>
    internal sealed record CommandPlan(string Program, IReadOnlyList<string>? ArgumentList, string? RawArguments, string GrantName)
    {
        /// <summary>Whether the command runs as a script in a shell (pwsh, cmd, wsl) rather than as a program.</summary>
        public bool IsScript { get; init; } = true;
    }

    /// <summary>
    /// The program and arguments for <paramref name="command"/> run through <paramref name="shell"/>: "pwsh",
    /// "powershell", "cmd" and "wsl" run it as a script; "none" runs it directly (its first word is the program).
    /// </summary>
    internal static CommandPlan PlanCommand(string shell, string command)
    {
        switch (shell.Trim().ToLowerInvariant())
        {
            case "pwsh":
            case "powershell":
                var exe = shell.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
                    ? CliAgent.FindOnPath("pwsh") ?? "powershell.exe"
                    : "powershell.exe";
                return new CommandPlan(exe,
                    ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                     "[Console]::OutputEncoding = [Text.Encoding]::UTF8; " + command], null, shell.Trim().ToLowerInvariant());
            case "cmd":
                return new CommandPlan("cmd.exe", null, "/d /s /c \"chcp 65001 >nul & " + command + "\"", "cmd");
            case "wsl":
                return new CommandPlan("wsl.exe", ["--", "bash", "-lc", command], null, "wsl");
            case "none":
            case "":
                var parts = CustomAgent.SplitArguments(command);
                if (parts.Count == 0) throw new ToolInputException("\"command\" is empty.");
                var name = Path.GetFileNameWithoutExtension(parts[0]).ToLowerInvariant();
                return new CommandPlan(parts[0], parts.Skip(1).ToList(), null, name) { IsScript = false };
            default:
                throw new ToolInputException($"Unknown shell \"{shell}\"; use pwsh, powershell, cmd, wsl or none.");
        }
    }

    private async Task<ToolOutcome> RunCommandAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken)
    {
        var command = String(input, "command");
        var shell = input.TryGetValue("shell", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "none" : "none";
        var plan = PlanCommand(shell, command);
        var cwd = input.ContainsKey("cwd") ? FullPath(input, "cwd")
            : Path.GetDirectoryName(main.SelectedTab?.SourcePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(cwd)) throw new ToolInputException($"No folder {cwd}.");
        var timeout = TimeSpan.FromSeconds(Math.Clamp(Int(input, "timeout_seconds", DefaultCommandTimeoutSeconds), 1, MaxCommandTimeoutSeconds));
        // The exact text an "only this command" grant matches: a script is told apart by its shell.
        var commandLine = plan.IsScript ? $"{plan.GrantName}: {command}" : command.Trim();

        var request = new PermissionRequest(AiAccess.Execute, plan.GrantName, CommandLine: commandLine, Detail: $"In folder: {cwd}");
        if (!await AllowedAsync(request, cancellationToken)) return Denied($"running {commandLine}");

        var result = await RunProcessAsync(plan, cwd, timeout, cancellationToken);
        return new ToolOutcome(result.Text, IsError: result.ExitCode != 0);
    }

    internal static async Task<(int ExitCode, string Text)> RunProcessAsync(CommandPlan plan, string cwd, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(plan.Program)
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (plan.RawArguments is not null) start.Arguments = plan.RawArguments;
        foreach (var argument in plan.ArgumentList ?? []) start.ArgumentList.Add(argument);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0"; // never wait for a password nobody can type

        var output = new StringBuilder();
        var truncated = false;
        void Append(string? line)
        {
            if (line is null) return;
            lock (output)
            {
                if (output.Length + line.Length + 1 > MaxCommandOutputChars)
                {
                    truncated = true;
                    return;
                }

                output.AppendLine(line);
            }
        }

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (-1, $"Could not start {plan.Program}: {ex.Message}");
        }

        process.StandardInput.Close(); // no input: a command that asks for some gets end-of-file instead of hanging
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            await process.WaitForExitAsync(CancellationToken.None); // let the output readers finish
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            cancellationToken.ThrowIfCancellationRequested(); // Stop in the chat
            lock (output) return (-1, $"{output}\nStopped after {timeout.TotalSeconds:N0} s (timeout).");
        }

        lock (output)
        {
            var text = output.Length == 0 ? "(no output)\n" : output.ToString();
            if (truncated) text += $"… output cut at {MaxCommandOutputChars:N0} characters.\n";
            return (process.ExitCode, $"{text}Exit code {process.ExitCode}.");
        }
    }
}
