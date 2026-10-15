# 03: Show the first screen of lines

**What to build:** After opening a file, the user immediately sees the first screen of lines in a monospaced font (Cascadia Mono, falling back to Consolas), with a line-number margin. `LogDocument` gains reading of line ranges through positional reads (`RandomAccess`) and a small LRU cache of decoded 64 KB blocks. This introduces the custom virtualized viewer control, which draws only the visible rows from a *line source* (a list of line numbers plus a line reader). There is no scrolling yet.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] `LogDocument` returns the text of any range of lines and the full text of a single line; tests cover the start, middle and end of a file and lines that span a block boundary
- [ ] The whole file is never loaded into memory; reads are positional and go through the block cache
- [ ] The first screen appears before indexing finishes, for a multi-GB file
- [ ] The viewer control renders only the visible rows and shows a line-number margin
- [ ] The viewer's data source is a line source abstraction (line numbers plus a reader), not a text document
