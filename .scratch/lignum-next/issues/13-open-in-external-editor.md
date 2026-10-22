# 13: Open the file in a text editor of choice

**What to build:** Open the current log in an external editor configured in settings, at the current line where the editor supports it.

**Blocked by:** None

**Status:** ready-for-agent

**Settings:** `ExternalEditor { Command, Arguments }` in `AppSettings`. The argument template takes `{file}` and `{line}`. Presets: Notepad, Notepad++ (`-n{line} "{file}"`), VS Code (`-g "{file}:{line}"`). Default: Notepad.

- [ ] File → Open in editor (Ctrl+Shift+E) and "Open in editor at this line" on the line context menu
- [ ] Settings UI to choose a preset or enter a custom command and arguments
- [ ] Template expansion is unit tested (quoting, missing `{line}`)
- [ ] Only `Process.Start`; Lignum never writes to or locks the log
- [ ] A missing editor shows a clear error, not a crash
