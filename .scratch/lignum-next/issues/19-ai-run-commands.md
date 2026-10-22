# 19: Run commands with user approval

**What to build:** Let the assistant run commands (git, PowerShell, cmd, Python, WSL, …) with the same approval model as ticket 17.

**Blocked by:** 17

**Status:** ready-for-agent

- [ ] MCP tool `run_command { shell: pwsh|cmd|git|python|wsl|exe, command, cwd }`
- [ ] Approval dialog shows the exact command line and working folder; grants per executable or per exact command
- [ ] Runs in the background, cancellable, with a timeout and an output cap; output streams to the chat
- [ ] The working folder needs a read grant
- [ ] Tests with a fake approver and a harmless command
