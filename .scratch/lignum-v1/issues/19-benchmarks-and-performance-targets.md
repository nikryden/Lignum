# 19: Benchmarks and performance targets

**What to build:** A separate benchmark project, run manually and never in CI, that generates realistic multi-GB log files and measures the targets for a 2 GB file on an SSD: first screen in under 1 s, full index in about 5–10 s, full regex search in about 10–20 s. Profile the code and tune it until the targets are met. Index memory stays reasonable (compact the offsets only if measurements show it is needed).

**Blocked by:** 11

**Status:** ready-for-agent

- [ ] The benchmark project generates test files of a configurable size and runs from the command line
- [ ] Results are reported for time to first screen, indexing time, plain-text search time, regex search time and peak memory
- [ ] The targets are met on the developer machine, or the gaps are documented with findings
- [ ] Benchmarks are excluded from the CI workflow
- [ ] `CLAUDE.md` documents how to run the benchmarks
