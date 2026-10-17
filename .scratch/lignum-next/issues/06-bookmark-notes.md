# 06: Bookmark notes

**What to build:** Let the user attach a short text note to a bookmarked line. The note is saved per log file, together with that file's bookmarks, and restored when the file is reopened. Notes are shown next to the bookmark and can be edited or removed.

**Blocked by:** None (can start immediately)

**Status:** done (in-place reading of uncompressed packages split out to ticket 10)

**Storage:** Use the existing per-file bookmarks file, `bookmarks/<sha256 of upper-cased full path>.json` in the settings folder (ticket v1-17). Add an optional `note` field to each bookmark object, `{ "line": 123, "note": "..." }`. The v1 schema was designed for this, and unknown fields are already kept on save, so there is no migration. A bookmark without a note stays valid.

- [x] Add or edit a note from the right-click menu ("Edit bookmark note…"); adding a note to an unbookmarked line also bookmarks it
- [x] Bookmarked lines with a note show a distinct margin marker, and the note appears as a tooltip on hover
- [x] Notes are saved per file and restored on reopen (view-model test with a temp settings folder)
- [x] Removing the bookmark removes its note; clearing the note keeps the bookmark
- [x] v1 bookmark files without notes load unchanged (test)
- [x] F2 / Shift+F2 navigation is unchanged

## Export and share with notes (`.lm` package)

The owner wants a log and its notes to be sendable to other people as one file. Proposal: a new package type, **`.lm`** (Lignum package), which is a zip containing:
- the log file, unchanged, under its original name
- `bookmarks.json`, with the same bookmark/note schema as above
- `manifest.json`: format version, original file name, size and SHA-256 of the log, encoding, created-by and date

- [x] File → Export with notes… writes a `.lm` file from the current tab (in the background, with progress and cancel; the original log is only read, never modified or locked)
- [x] Lignum opens `.lm` files (File → Open, drag-and-drop, command line) and shows the log with its bookmarks and notes
- [x] On open, the log's SHA-256 is checked against the manifest; a mismatch shows a warning
- [x] Notes opened from a package are kept with that package and do not overwrite the recipient's own bookmarks for any local file with the same path
- [x] Round-trip test: export, then open, gives the same lines, bookmarks and notes

**Constraint to resolve (large files):** Lignum reads files by position and never loads a whole file into memory (CLAUDE.md). A compressed (deflated) zip entry can't be read by position, so opening a `.lm` must either:
- **(a)** extract the log to a temp folder first, then open it normally (simple; needs disk space and time for multi-GB logs), or
- **(b)** store the log entry *uncompressed* inside the zip and read it in place by offset (instant to open; no size saving).

A reasonable default is (a), with the temp copy deleted when the tab closes, and possibly (b) as an export option for huge logs.

## Open questions
- Is a bookmarks/notes list pane wanted (all notes in the file, click to jump)?
- Compression choice above: (a), (b), or both?
- Should a recipient be able to add their own notes and re-export the package?
- Is the `.lm` file association (double-click to open) wanted? It is out of scope for v1 (no installer).

## Decisions (2026-10-17)
- Compression: both. Compressed (a) by default; uncompressed (b) as an export option for huge logs.
- Notes list pane: yes. A pane lists every bookmark and note in the file; clicking one jumps to its line.
- Re-export: yes. Notes added in a package tab are kept with that package and included when it is exported again.
