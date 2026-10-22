# 17: AI permission system (ADR + permission store)

**What to build:** A permission model that lets the AI read files and folders outside the open logs, write to disk and run commands, only with the user's approval.

**Blocked by:** None

**Status:** ready-for-agent

**Decision (ADR 0002):** Lignum enforces permissions itself instead of relying on each CLI's own flags. The agents' native write/shell tools stay off; Lignum offers MCP tools (tickets 18, 19) that check every call against a permission store and ask the user through an approval dialog. This works the same for every agent (print-mode CLIs cannot show their own prompts in our UI) and lets Lignum refuse writes to open log files.

**Grant:** scope (folder path, or executable / exact command), access (read, write, execute), lifetime (once, this session, always).

- [ ] `docs/adr/0002-ai-permissions.md`
- [ ] Permission store: session grants in memory, "always" grants in settings
- [ ] Approval dialog: Allow once / Allow this session / Always allow / Deny
- [ ] Settings → AI permissions: list "always" grants with Revoke (and Revoke all)
- [ ] Path matching tests: subfolders, `..`, case, trailing separators, symlinks/junctions resolved before checking
