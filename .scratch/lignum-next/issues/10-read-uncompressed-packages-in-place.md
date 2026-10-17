# 10: Open uncompressed .lm packages without extracting

**What to build:** An uncompressed `.lm` package (File > Export with notes, uncompressed) stores the log as-is inside the zip. Today it is still extracted to a temp folder before it opens (at plain copy speed). Instead, open it instantly by reading the log entry in place, by offset, from the zip file.

**Blocked by:** 06

**Status:** needs-triage

- [ ] Core: `LogDocument` can read a byte range of a larger file (start offset and length) instead of a whole file; indexing, reading, search and change detection all use that window
- [ ] The zip's local header is parsed to find where the stored entry's data starts (no third-party library)
- [ ] An uncompressed package opens with no temp copy; a compressed one is still extracted
- [ ] The SHA-256 check still runs, in the background, and warns on a mismatch
- [ ] Tests: in-place open gives the same lines as an extracted open; a compressed entry is never read in place

Estimate: ~40k tokens.
