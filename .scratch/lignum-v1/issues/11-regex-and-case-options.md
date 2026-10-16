# 11: Regex and case options

**What to build:** The find box gains a regex toggle and a case-sensitivity toggle. Regex search uses compiled .NET `Regex` with a match timeout, so a catastrophic pattern produces a clear error instead of hanging the app. An optional NonBacktracking engine is available. An invalid pattern shows an error without starting a search.

**Blocked by:** 10

**Status:** resolved

- [x] Regex and case toggles work for find all and F3 navigation
- [x] A catastrophic pattern times out and reports an error; the app stays responsive (tested)
- [x] An invalid regex shows a validation error
- [x] The NonBacktracking option can be selected and produces the same results for supported patterns (tested)

## Comments

Core: `QueryRule.NonBacktracking` / `Query.Find(..., nonBacktracking)`; NonBacktracking-unsupported constructs are rethrown as `ArgumentException`. Tests cover regex+case, timeout (`QueryState.Failed` + `RegexMatchTimeoutException`), invalid patterns and NonBacktracking parity.
App: `SearchViewModel` gains `IsRegex`/`MatchCase`/`UseNonBacktracking` (Alt+R/C/B), `FindTextError`, reruns the shown search on toggle, clears the highlight and shows a clear message on timeout, and highlights with a 50 ms regex timeout.
Find-bar toggles (Aa, .*, NB) and the error line were not checked visually (manual runs are disallowed); verify by hand.
