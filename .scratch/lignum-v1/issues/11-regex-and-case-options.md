# 11: Regex and case options

**What to build:** The find box gains a regex toggle and a case-sensitivity toggle. Regex search uses compiled .NET `Regex` with a match timeout, so a catastrophic pattern produces a clear error instead of hanging the app. An optional NonBacktracking engine is available. An invalid pattern shows an error without starting a search.

**Blocked by:** 10

**Status:** ready-for-agent

- [ ] Regex and case toggles work for find all and F3 navigation
- [ ] A catastrophic pattern times out and reports an error; the app stays responsive (tested)
- [ ] An invalid regex shows a validation error
- [ ] The NonBacktracking option can be selected and produces the same results for supported patterns (tested)
