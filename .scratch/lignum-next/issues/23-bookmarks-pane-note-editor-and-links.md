# 23: Bookmarks pane: note editor and links

**What to build:** Edit and copy the selected bookmark's note in the pane, and open links written in notes.

**Blocked by:** 22

**Status:** done

- [x] The selected bookmark's note can be edited in place in the pane (saved like "Edit bookmark note…")
- [x] Note text can be selected, copied and pasted (a real text box, not a label)
- [x] Links in a note are shown as links and open with Ctrl+left click:
  - `http(s)://…` opens in the default browser
  - a local or UNC file path (`C:\…`, `\server\…`) opens in a Lignum tab
  - a folder path opens in Explorer
- [x] A path counts as a link when it looks like one; if it does not exist when clicked, show an error
- [x] A link's right-click menu: "Open in tab" (files), "Show in folder" (Explorer `/select`), "Copy link"
- [x] Tests for link detection (URLs, drive paths, UNC paths, trailing punctuation)
