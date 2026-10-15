# 15: Right-click menu and copy to search

**What to build:** Right-clicking selected text offers Copy, Copy to search, Add as include filter, Add as exclude filter, Add as highlight rule, and Show full line. Ctrl+Shift+C ("copy to search") puts the selection into the find box and runs find all. With no selection it uses the word under the cursor; for a multi-line selection it uses the first line. This lets a user trace an ID or hide noise in one or two actions.

**Blocked by:** 07, 08, 12, 14

**Status:** ready-for-agent

- [ ] The menu shows the listed items in both the main view and the results pane
- [ ] Ctrl+Shift+C picks its search text correctly in each case (selection, word under cursor, first line) (view-model tests)
- [ ] Add as include/exclude filter creates a plain-text rule from the selection and updates the results
- [ ] Add as highlight rule adds a rule to the active preset
- [ ] Show full line opens the detail pane
