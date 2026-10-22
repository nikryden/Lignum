using System.Text.Json.Serialization;

namespace Lignum.App.Assistant.Permissions;

// AI permissions (ticket 17, ADR 0002): what an AI agent may do beyond reading the open logs. Every powerful tool
// (files, commands) asks PermissionStore first; without a grant the user is asked through an IPermissionApprover.

/// <summary>What the agent wants to do. <see cref="Write"/> implies <see cref="Read"/>; <see cref="Execute"/> stands alone.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AiAccess>))]
public enum AiAccess
{
    Read,
    Write,
    Execute,
}

/// <summary>What a grant covers.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GrantScopeKind>))]
public enum GrantScopeKind
{
    /// <summary>One file (canonical full path).</summary>
    File,

    /// <summary>A folder and everything under it (canonical full path).</summary>
    Folder,

    /// <summary>A program, with any arguments ("git", "pwsh").</summary>
    Executable,

    /// <summary>One exact command line.</summary>
    Command,
}

/// <summary>How long an approval lasts.</summary>
public enum GrantLifetime
{
    /// <summary>This call only; nothing is stored.</summary>
    Once,

    /// <summary>Until Lignum exits.</summary>
    Session,

    /// <summary>Saved in the settings until revoked.</summary>
    Always,
}

/// <summary>One stored permission. Saved in settings for <see cref="GrantLifetime.Always"/>.</summary>
public sealed class PermissionGrant
{
    public AiAccess Access { get; set; }

    public GrantScopeKind Kind { get; set; }

    /// <summary>The canonical path, the program name or the command line, depending on <see cref="Kind"/>.</summary>
    public string Scope { get; set; } = "";

    public DateTimeOffset Granted { get; set; }

    /// <summary>"Write folder C:\repo", for lists.</summary>
    public override string ToString() => $"{Access} {Kind.ToString().ToLowerInvariant()} {Scope}";
}

/// <summary>
/// One thing an agent asks to do. For <see cref="AiAccess.Read"/> and <see cref="AiAccess.Write"/>,
/// <see cref="Target"/> is a path (a file or a folder, see <see cref="IsFolder"/>). For <see cref="AiAccess.Execute"/>
/// it is the program name and <see cref="CommandLine"/> the whole command.
/// </summary>
/// <param name="Detail">More for the user to see before deciding: a diff, the working folder.</param>
public sealed record PermissionRequest(
    AiAccess Access, string Target, bool IsFolder = false, string? CommandLine = null, string? Detail = null);

/// <summary>A scope the approval dialog offers, e.g. "This file" → File C:\a\b.log.</summary>
public sealed record ScopeOption(string Label, GrantScopeKind Kind, string Scope)
{
    public override string ToString() => Label;
}

/// <summary>The user's answer: allowed for how long and on which scope. Null from the approver means denied.</summary>
public sealed record ApprovalAnswer(GrantLifetime Lifetime, ScopeOption Scope);

/// <summary>Asks the user (a dialog in the app; a fake in tests).</summary>
public interface IPermissionApprover
{
    /// <summary>The user's answer, or null when they deny. <paramref name="options"/> has at least one entry.</summary>
    Task<ApprovalAnswer?> AskAsync(PermissionRequest request, IReadOnlyList<ScopeOption> options, CancellationToken cancellationToken);
}
