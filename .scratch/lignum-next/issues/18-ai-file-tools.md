# 18: AI file read/write tools

**What to build:** MCP tools for the assistant to read files and folders anywhere (including other repositories) and write files, gated by the permission store.

**Blocked by:** 17

**Status:** ready-for-agent

- [ ] `list_dir`, `read_file` (line range, size cap), `write_file`, `edit_file` (exact replace)
- [ ] Each call checks the store and asks when there is no grant
- [ ] Write approval shows a diff preview
- [ ] Writes to a file open as a log are always refused (hard constraint)
- [ ] Tool tests with a fake approver
