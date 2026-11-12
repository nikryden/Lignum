# 36: More bookmark colors

**What to build:** Bookmarks support more colors than the current five.

**Blocked by:** none

**Status:** ready-for-agent

## Behaviour

- Colors: Blue, Red, Yellow, Green, Purple (existing), plus Orange, Pink, Teal, Cyan and Gray.
- Every place a color is chosen or listed gets the new colors: margin context menu, bookmark note dialog, bookmarks pane filter, **Remove bookmarks of color**, **Show only colors**, and export.
- Each color has a dark and a light theme variant that is readable as a margin marker and as a row tint.
- Existing bookmark files load unchanged. New values are appended to the enum, and names are serialized as before.

## Design

- Extend `BookmarkColor` in `Settings/BookmarksFile.cs`. Add brushes to both theme files. Find any `switch` over the enum (including `AssistantTools.cs`) and the AI tool schema that lists colors.
- Tests: serialization round-trip of the new values. Old files still load.
