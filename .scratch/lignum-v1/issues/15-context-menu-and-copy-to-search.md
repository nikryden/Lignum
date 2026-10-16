# 15: Right-click menu and copy to search

**What to build:** Right-clicking selected text offers Copy, Copy to search, Add as include filter, Add as exclude filter, Add as highlight rule, and Show full line. Ctrl+Shift+C ("copy to search") puts the selection into the find box and runs find all. With no selection it uses the word under the cursor; for a multi-line selection it uses the first line. This lets a user trace an ID or hide noise in one or two actions.

**Blocked by:** 07, 08, 12, 14

**Status:** resolved

- [x] The menu shows the listed items in both the main view and the results pane
- [x] Ctrl+Shift+C picks its search text correctly in each case (selection, word under cursor, first line) (view-model tests)
- [x] Add as include/exclude filter creates a plain-text rule from the selection and updates the results
- [x] Add as highlight rule adds a rule to the active preset
- [x] Show full line opens the detail pane

## Comments

Commands live in `LogTabViewModel.ContextMenu.cs` (`CopyToSearch`, `AddIncludeFilter`, `AddExcludeFilter`, `AddHighlightRule`, each taking the view's `TextSelection`, null = main view); `PickSearchTextAsync` picks the selection, its first line, or the word under the caret (letters/digits/`_`), capped at 10,000 chars; with regex on, copy to search escapes the text. Menus (main view and results pane) are in `LogTabView.xaml`; a right-click outside the selection moves the caret there first. Tested in `ContextMenuTests` (view-model level) plus a throwaway off-screen STA smoke test that loaded both menus and ran Show full line.
"Toggle bookmark" is a disabled placeholder for ticket 17. Visual check of the menus with real mouse input was not done (manual).
