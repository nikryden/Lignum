using Lignum.App.Settings;

namespace Lignum.App.Assistant.Permissions;

/// <summary>
/// The AI permission grants (ADR 0002): "always" grants live in <see cref="AppSettings.AiPermissions"/>, session
/// grants in memory. <see cref="RequestAsync"/> is the one gate every powerful tool goes through.
/// </summary>
public sealed class PermissionStore(SettingsFile<AppSettings>? settings)
{
    private readonly List<PermissionGrant> _session = [];
    private readonly List<PermissionGrant> _unsaved = []; // "always" grants when there is no settings file (tests)

    /// <summary>Raised after a grant is added or revoked.</summary>
    public event EventHandler? Changed;

    /// <summary>Grants saved in the settings, until revoked.</summary>
    public IReadOnlyList<PermissionGrant> AlwaysGrants => settings?.Value.AiPermissions ?? _unsaved;

    /// <summary>Grants that end when Lignum exits.</summary>
    public IReadOnlyList<PermissionGrant> SessionGrants => _session;

    /// <summary>Whether a session or always grant covers <paramref name="request"/>.</summary>
    public bool IsAllowed(PermissionRequest request)
    {
        var target = Normalize(request);
        return AlwaysGrants.Concat(_session).Any(g => Covers(g, request.Access, target, request.CommandLine));
    }

    /// <summary>
    /// True when <paramref name="request"/> may go ahead: a grant covers it, or the user allows it now (which stores a
    /// grant unless they chose "once"). False when they deny.
    /// </summary>
    public async Task<bool> RequestAsync(PermissionRequest request, IPermissionApprover approver, CancellationToken cancellationToken)
    {
        if (IsAllowed(request)) return true;
        var answer = await approver.AskAsync(request, ScopeOptions(request), cancellationToken);
        if (answer is null) return false;
        if (answer.Lifetime != GrantLifetime.Once) Add(request.Access, answer.Scope.Kind, answer.Scope.Scope, answer.Lifetime);
        return true;
    }

    public void Add(AiAccess access, GrantScopeKind kind, string scope, GrantLifetime lifetime)
    {
        if (lifetime == GrantLifetime.Once) return;
        var grant = new PermissionGrant
        {
            Access = access, Kind = kind, Granted = DateTimeOffset.Now,
            Scope = kind is GrantScopeKind.File or GrantScopeKind.Folder ? CanonicalPath.Of(scope) : scope.Trim(),
        };
        if (lifetime == GrantLifetime.Session)
        {
            _session.Add(grant);
        }
        else if (settings is null)
        {
            _unsaved.Add(grant);
        }
        else
        {
            settings.Value.AiPermissions.Add(grant);
            settings.Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes <paramref name="grant"/>, whether it is a session or an always grant.</summary>
    public void Revoke(PermissionGrant grant)
    {
        var removed = _session.Remove(grant) | _unsaved.Remove(grant);
        if (settings is not null && settings.Value.AiPermissions.Remove(grant))
        {
            settings.Save();
            removed = true;
        }

        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes every grant, session and always.</summary>
    public void RevokeAll()
    {
        _session.Clear();
        _unsaved.Clear();
        if (settings is not null && settings.Value.AiPermissions.Count > 0)
        {
            settings.Value.AiPermissions.Clear();
            settings.Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The scopes the approval dialog offers, narrowest first: the file then its folder (or the folder then its
    /// parent); the exact command then every command of the program.
    /// </summary>
    public static IReadOnlyList<ScopeOption> ScopeOptions(PermissionRequest request)
    {
        if (request.Access == AiAccess.Execute)
        {
            List<ScopeOption> commands = [];
            if (!string.IsNullOrWhiteSpace(request.CommandLine))
                commands.Add(new ScopeOption("Only this exact command", GrantScopeKind.Command, request.CommandLine.Trim()));
            commands.Add(new ScopeOption($"Any {request.Target} command", GrantScopeKind.Executable, request.Target.Trim()));
            return commands;
        }

        var path = CanonicalPath.Of(request.Target);
        var parent = Path.GetDirectoryName(path);
        List<ScopeOption> options = request.IsFolder
            ? [new ScopeOption("This folder and its subfolders", GrantScopeKind.Folder, path)]
            : [new ScopeOption("Only this file", GrantScopeKind.File, path)];
        if (parent is not null)
            options.Add(new ScopeOption(request.IsFolder ? "Its parent folder and everything in it" : "Its folder and subfolders",
                GrantScopeKind.Folder, parent));
        return options;
    }

    private static string Normalize(PermissionRequest request) =>
        request.Access == AiAccess.Execute ? request.Target.Trim() : CanonicalPath.Of(request.Target);

    private static bool Covers(PermissionGrant grant, AiAccess access, string target, string? commandLine)
    {
        if (access == AiAccess.Execute)
        {
            if (grant.Access != AiAccess.Execute) return false;
            return grant.Kind switch
            {
                GrantScopeKind.Executable => string.Equals(grant.Scope, target, StringComparison.OrdinalIgnoreCase),
                GrantScopeKind.Command => commandLine is not null && string.Equals(grant.Scope, commandLine.Trim(), StringComparison.Ordinal),
                _ => false,
            };
        }

        if (grant.Access == AiAccess.Execute || (access == AiAccess.Write && grant.Access != AiAccess.Write)) return false;
        return grant.Kind switch
        {
            GrantScopeKind.File => string.Equals(grant.Scope, target, StringComparison.OrdinalIgnoreCase),
            GrantScopeKind.Folder => CanonicalPath.IsUnder(target, grant.Scope),
            _ => false,
        };
    }
}
