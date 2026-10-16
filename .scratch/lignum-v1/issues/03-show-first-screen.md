# 03: Show the first screen of lines

**What to build:** After opening a file, the user immediately sees the first screen of lines in a monospaced font (Cascadia Mono, falling back to Consolas), with a line-number margin. `LogDocument` gains reading of line ranges through positional reads (`RandomAccess`) and a small LRU cache of decoded 64 KB blocks. This introduces the custom virtualized viewer control, which draws only the visible rows from a *line source* (a list of line numbers plus a line reader). There is no scrolling yet.

**Blocked by:** 02

**Status:** resolved

- [x] `LogDocument` returns the text of any range of lines and the full text of a single line; tests cover the start, middle and end of a file and lines that span a block boundary
- [x] The whole file is never loaded into memory; reads are positional and go through the block cache
- [x] The first screen appears before indexing finishes, for a multi-GB file
- [x] The viewer control renders only the visible rows and shows a line-number margin
- [x] The viewer's data source is a line source abstraction (line numbers plus a reader), not a text document

## Comments
- Core: `LogDocument.ReadLine(line)` / `ReadLines(first, count)` (in `LogDocument.Reading.cs`) read through an internal LRU cache of 64 KB raw blocks (`Reading/BlockCache.cs`); requests larger than half the cache bypass it. Lines are readable as soon as indexed (tested from a `LinesAdded` handler).
- App: `Viewer/LineSource` (`ILineList` + `ILineReader`, async `ReadRowsAsync`) and `Viewer/LogView`, a `FrameworkElement` drawing only visible rows with `FormattedText` and a line-number margin. `MainViewModel.Lines` feeds it.
- Manual check: smoke-tested on a 278 MB generated log (first screen rendered correctly, Cascadia Mono, margin). Not checked by hand on a multi-GB file; the first screen depends only on the first 1 MB block being indexed.
