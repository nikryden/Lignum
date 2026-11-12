# 30: One Settings window

**What to build:** A single **Settings** window replaces the separate settings dialogs. A section list on the left, the selected page on the right.

**Blocked by:** none

**Status:** ready-for-agent

## Behaviour

- Open with **View ▸ Settings…** (Ctrl+,). It is modeless, has one instance and remembers the last page.
- Pages:
  - **Appearance:** theme (Windows / Light / Dark), font (today's `FontDialog` content), tab width, zoom reset.
  - **Highlight rules:** today's `HighlightRulesDialog` content.
  - **Syntax schemes:** today's `SyntaxSchemesDialog` content.
  - **External editor:** today's `ExternalEditorDialog` content.
  - **AI:** permissions (today's `AiPermissionsDialog`) and **Connect an AI tool** (ticket 31).
- Changes apply live, as they do in the dialogs today. Pages that currently have OK/Cancel keep per-page Apply/Revert semantics so nothing is lost.
- The View menu loses Font…, Highlight rules…, Syntax schemes…, External editor… and AI permissions…, and gets one **Settings…** item. The Theme and Tab width submenus stay as quick toggles.
- Other entry points (for example the context menu "highlight rules", or a tab's syntax override) open Settings on the matching page.
- Works in both themes.

## Design

- `SettingsWindow` with a `ListBox` of pages and a `ContentControl`. Each page is a `UserControl` extracted from the existing dialog XAML. The dialog view models are reused unchanged where possible.
- `SettingsWindowViewModel.Show(page)` brings the existing window to the front and switches to the page.
- Delete the old dialog windows once their content has moved.
- Tests: view-model tests for page switching and the single-instance behaviour.
