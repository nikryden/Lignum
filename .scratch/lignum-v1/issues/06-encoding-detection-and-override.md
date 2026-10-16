# 06: Encoding detection and override

**What to build:** Logs in UTF-8 (with or without BOM), UTF-16 LE/BE and Windows-1252 display correctly without any configuration. The detected encoding is shown in the status bar. Clicking it lets the user pick another encoding, which re-indexes the file. Detection order: BOM, then UTF-8 validity, then UTF-16 from the pattern of zero bytes, with Windows-1252 as the fallback. The line index must handle UTF-16 newlines.

**Blocked by:** 03, 05

**Status:** resolved

- [x] `LogDocument` reports the detected encoding; tests cover each supported encoding, with and without BOM
- [x] Line splitting is correct for UTF-16 LE and BE files
- [x] Overriding the encoding re-indexes and re-renders the file
- [x] The status bar shows the encoding and offers the override on click

## Comments
- Core: `LogDocument.Open(path, LogEncoding?)`, `FileEncoding`/`HasByteOrderMark`/`IsEncodingOverridden`; detection (BOM, UTF-8 validity, UTF-16 zero pattern, 1252 fallback) runs on the indexer thread on the first 64 KB. All decoding and line-break trimming goes through internal `Text/TextCodec` (`LogDocument.Codec`); the indexer splits UTF-16 on whole 2-byte newline units.
- Override = reopen in the same tab with the chosen encoding (kept for later reloads); "Detect automatically" clears it. Tests: `EncodingTests` (Core, real files per encoding, with/without BOM) and `EncodingStatusTests` (App).
- The status-bar encoding menu (MainWindow.xaml) builds but was not checked visually (no manual app runs allowed); verify by hand.
