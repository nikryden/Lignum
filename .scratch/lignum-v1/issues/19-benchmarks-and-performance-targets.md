# 19: Benchmarks and performance targets

**What to build:** A separate benchmark project, run manually and never in CI, that generates realistic multi-GB log files and measures the targets for a 2 GB file on an SSD: first screen in under 1 s, full index in about 5–10 s, full regex search in about 10–20 s. Profile the code and tune it until the targets are met. Index memory stays reasonable (compact the offsets only if measurements show it is needed).

**Blocked by:** 11

**Status:** resolved

- [x] The benchmark project generates test files of a configurable size and runs from the command line
- [x] Results are reported for time to first screen, indexing time, plain-text search time, regex search time and peak memory
- [x] The targets are met on the developer machine, or the gaps are documented with findings
- [x] Benchmarks are excluded from the CI workflow
- [x] `CLAUDE.md` documents how to run the benchmarks

## Comments

`benchmarks/Lignum.Benchmarks` (console app, measures through `LogDocument` only; generator tested in `tests/Lignum.Benchmarks.Tests`). It is not a test project, so `dotnet test`/CI never run it.
Measured 2026-10-16, 2.00 GB generated file (17.2M lines), warm OS cache, Release: first screen 0.016 s (target < 1 s), full index 0.62 s (~3.3 GB/s; target 5-10 s), plain search 0.55 s, regex `\[ERROR\] \[thread-\d+\] \w+: .*timed out` 1.58 s (NonBacktracking 1.70 s; target 10-20 s). Managed heap after indexing 132 MB (8 bytes/line), peak working set 277 MB.
All targets met by a wide margin, so no tuning or offset compaction was done. Cold-cache numbers were not measured (no way to drop the file cache without admin); on cold cache expect index/search to be bound by SSD read speed.
