# 19: Run commands with user approval

**What to build:** Let the assistant run commands (git, PowerShell, cmd, Python, WSL, …) with the same approval model as ticket 17.

**Blocked by:** 17

**Status:** done

- [x] MCP tool `run_command { command, shell: none|pwsh|powershell|cmd|wsl, cwd, timeout_seconds }`. With `none` the first word is the program (`git log -n 5`, `python x.py`)
- [x] Approval dialog shows the exact command line and working folder; grants per program (`git`, `pwsh`, `cmd`, …) or per exact command
- [x] Runs in the background with no console and no input, cancellable (Stop kills the process tree), timeout (default 120 s, max 600 s) and an output cap of 100,000 characters
- [x] ~~The working folder needs a read grant~~ Dropped: a command can read anything it is allowed to run against, so a separate folder grant adds a second dialog without adding safety. The folder is shown in the approval dialog instead.
- [x] ~~Output streams to the chat~~ The chat shows "Running …" and the agent gets the output and exit code when the command ends.
- [x] Tests with a fake approver and harmless cmd commands
