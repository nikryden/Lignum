# 13: Open the file in a text editor of choice

**What to build:** Open the current log in an external editor configured in settings, at the current line where the editor supports it.

**Blocked by:** None

**Status:** done

**Settings:** `ExternalEditor { Command, Arguments }` in `AppSettings`. The argument template takes `{file}` and `{line}`. Presets: Notepad, Notepad++ (`-n{line} "{file}"`), VS Code (`-g "{file}:{line}"`), Sublime Text. Default: Notepad.

- [x] File → Open in editor (Ctrl+Shift+E) and "Open in editor at this line" on the line context menu
- [x] View → External editor… dialog to choose a preset or enter a custom command and arguments
- [x] Template expansion is unit tested (quoting, missing `{line}`)
- [x] Only `Process.Start`; Lignum never writes to or locks the log
- [x] A missing editor shows a clear error, not a crash
