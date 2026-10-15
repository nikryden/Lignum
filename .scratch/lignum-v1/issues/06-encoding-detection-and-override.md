# 06: Encoding detection and override

**What to build:** Logs in UTF-8 (with or without BOM), UTF-16 LE/BE and Windows-1252 display correctly without any configuration. The detected encoding is shown in the status bar. Clicking it lets the user pick another encoding, which re-indexes the file. Detection order: BOM, then UTF-8 validity, then UTF-16 from the pattern of zero bytes, with Windows-1252 as the fallback. The line index must handle UTF-16 newlines.

**Blocked by:** 03, 05

**Status:** ready-for-agent

- [ ] `LogDocument` reports the detected encoding; tests cover each supported encoding, with and without BOM
- [ ] Line splitting is correct for UTF-16 LE and BE files
- [ ] Overriding the encoding re-indexes and re-renders the file
- [ ] The status bar shows the encoding and offers the override on click
