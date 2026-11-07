# 26: Pick the syntax scheme of a tab from the status bar

**What to build:** A status bar item that shows the syntax scheme of the current tab and lets you pick another one for that tab, like the language picker in VS Code.

**Blocked by:** 25 (syntax schemes)

**Status:** needs-triage

## Behaviour

- The status bar shows the current tab's scheme name. It shows "Plain" for a file that uses the whole-line highlight rules (`.txt`, `.log`, `.lm`, or an extension without a scheme).
- Clicking the item opens a menu with "Plain (highlight rules)" and every scheme, with the current one checked. Picking one recolors that tab at once. It works the same way as the encoding override.
- The choice applies to that tab only and is not remembered. Reopening the file goes back to choosing by extension.
- This is how a `.log` or `.txt` file can be shown with a syntax scheme, for example a JSON-lines log as JSON. The default for those types is unchanged.

## Done when

- [ ] The status bar shows the scheme, and picking another one recolors only that tab
- [ ] Closing and reopening the file goes back to the scheme for its extension
- [ ] View-model tests for the override and the reset on reopen
