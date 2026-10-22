# 18: AI file read/write tools

**What to build:** MCP tools for the assistant to read files and folders anywhere (including other repositories) and write files, gated by the permission store.

**Blocked by:** 17

**Status:** done

- [x] `list_dir`, `read_file` (line range, size cap), `write_file`, `edit_file` (exact replace)
- [x] Each call checks the store and asks when there is no grant
- [x] Write approval shows the new content, or a -/+ preview for edit_file
- [x] Writes to a file open as a log (or Lignum's settings folder, which holds the grants) are always refused
- [x] Tool tests with a fake approver
