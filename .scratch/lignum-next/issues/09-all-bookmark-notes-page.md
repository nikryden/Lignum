# 09: A page with every bookmark note

**What to build:** A page that lists all bookmark notes, so the user can see them in one place and click a note to go to its line. If the note's file is not open, clicking opens it in a tab first.

The per-file bookmarks pane (ticket 06, Go > Bookmarks pane) already lists the notes of the current file. This ticket goes further: notes from every log that has them, read from the `bookmarks/*.json` files in the settings folder.

**Blocked by:** 06 (notes)

**Status:** needs-triage

- [ ] A page (window or tab) lists every note with its file name, line number and text, grouped by file
- [ ] Clicking a note opens the file (or switches to its tab) at that line
- [ ] Notes whose file no longer exists are shown greyed out, and can be removed
- [ ] Loading the list runs in the background and never blocks the UI

## Open questions
- Its own window, a tab, or a bigger version of the bookmarks pane?
- Should it include notes from opened `.lm` packages?
- Is search or filtering inside the list wanted?
