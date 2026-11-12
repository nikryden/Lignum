# 2. Lignum enforces AI permissions itself

Date: 2026-10-22. Status: accepted.

## Context

Users want the in-app AI (Claude Code, Copilot CLI, Cursor CLI, Codex CLI or a custom agent) to read files in other folders and repositories, write files, and run commands such as git, PowerShell, Python and WSL. Each time, the user should approve: once, for this session, or always. "Always" grants must be easy to revoke later.

Each CLI has its own permission flags and config files, and they differ in shape and maturity. More importantly, Lignum runs them headless (`-p` print mode), where they cannot show their own approval prompts. Lignum also has a hard rule that it never writes to, or locks, a log file. An agent's own write tool could break that rule without Lignum knowing.

## Decision

- **Agents keep only Lignum's tools.** Their built-in file, shell and web tools stay off, as they are today (`--tools ""` for Claude Code, a read-only sandbox for Codex, and so on).
- **Lignum adds the powerful tools as MCP tools:** `list_dir`, `read_file`, `write_file` and `edit_file` (ticket 18), and `run_command` (ticket 19). Each call goes through one **permission store** in the running app before it does anything. The older tools that reach outside the open tabs, `find_in_files` (a folder) and `open_file` (a file not already open), need a read grant too.
- **A grant** is an access (read, write or execute) on a scope: a file, a folder and its subfolders, an executable (any arguments), or one exact command line. Write implies read. Execute is separate.
- **Lifetimes:** *once* (nothing is stored), *this session* (in memory, gone when Lignum exits) and *always* (saved in `settings.json` under `aiPermissions`).
- **Approval:** with no matching grant, Lignum shows a modal dialog in the app. It names the agent's request and offers the scopes that make sense (this file or its folder; this exact command or every command of that program). The buttons are Allow once, Allow for this session, Always allow and Deny.
- **Revoking:** View → Settings… → AI (Ctrl+,) lists the session and always grants. Each has Revoke, and there is a Revoke all.
- **Paths are compared in canonical form:** a full path with `..` resolved, symlinks and junctions resolved to their final target (`GetFinalPathNameByHandle` on the deepest part that exists), compared without case. A grant on a folder covers a path only at a separator boundary, so `C:\a` does not cover `C:\ab`.
- **Log files stay read-only:** `write_file` and `edit_file` refuse any file open as a log in a tab, whatever grants exist.

## Consequences

- Every agent gets the same model, including agents connected from outside through `Lignum.exe --mcp`, because the check is in the app, not in the agent.
- Agents work only through Lignum's tools, so their own richer tools (for example Claude Code's editor) are not used. Ticket 21 may revisit this.
- An approval dialog blocks the tool call until the user answers. The agent waits, and Stop in the chat still cancels.
- "Always" grants are stored in plain text in the user's settings folder. They are only as safe as that folder.
