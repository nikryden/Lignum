# 25: Syntax coloring by file type

**What to build:** Notepad++ / VS Code style syntax coloring for source and data files, chosen by file extension, with a page to manage the schemes (add, edit, import, export).

**Blocked by:** none

**Status:** needs-triage

## Behaviour

- A **syntax scheme** has a name, a list of extensions (`.cs;.csx`) and ordered **token rules**. Each rule has a name (Comment, String, Keyword…), a regex, and a text color for the light theme and for the dark theme. Optionally it can be bold or italic.
- Opening a file whose extension has a scheme colors parts of each line with that scheme. The extension match ignores case.
- **`.txt`, `.log` and `.lm` never use a syntax scheme.** They keep working as today, with the whole-line highlight rules of the active preset. No scheme can claim these extensions.
- A file whose extension has no scheme also uses the whole-line rules of the active preset, as today.
- A file that uses a scheme does not use the whole-line rules. Search matches, the selection, hand-set line colors ("Highlight line…") and bookmarks still draw on top as today.
- Theme switches recolor at once, as the whole-line rules do.

## Tokenizing (the constraint that shapes it)

- **Each line is colored on its own**, from its text alone. The viewer reads only the visible lines of files up to many GB, so it cannot run a lexer from the start of the file the way Notepad++ does.
- So a construct that spans lines is colored only on the lines where its markers are visible: the `/*` and `*/` lines of a block comment, the first line of a multi-line string. The lines in between stay plain. This is a known, accepted limit for v1.
- At each position the earliest match wins. Among matches that start at the same position, the rule that comes first in the list wins. Because of this, comment and string rules go first, so keywords inside strings stay string-colored.
- Regexes run with a match timeout (hard constraint). A rule that times out or is invalid is skipped, and the editor shows the problem, as the highlight rules dialog does.
- Only visible lines are tokenized, and the result is cached per line, the same way display text is. Lines longer than the display cut-off (about 10,000 characters) are tokenized only up to that cut-off.

## Schemes page

- View > **Syntax schemes…** opens a window:
  - On the left, the list of schemes: add, duplicate, delete.
  - On the right, the name, the extensions and the token rule grid. The grid uses the same color fields as the highlight rules dialog, plus bold and italic, an "On" checkbox, a Problem column, and Move up / Move down.
  - A sample box shows a few typed lines colored live.
- **Import…** reads a `.json` file with one or more schemes. A scheme with a name that is already used asks whether to replace it or keep both. **Export…** writes the selected scheme, or all of them, to a `.json` file.
- **Factory defaults** brings back the built-in schemes, after asking for confirmation, the same way the highlight rules button does.
- Changes apply and are saved at once, to `syntax-schemes.json` next to `highlight-presets.json`.
- Picking another scheme for one tab from the status bar is a follow-up: ticket 26.

## Built-in schemes

C#, C/C++, Java, Kotlin, Go, Rust, Swift, JavaScript, TypeScript, Python, Ruby, PHP, PowerShell, Bash/shell, Batch (`.bat;.cmd`), SQL, JSON, XML/XAML/csproj/config, HTML, CSS/SCSS, YAML, TOML/INI/`.properties`/`.env`, Markdown, Dockerfile, Makefile, Diff/patch, CSV/TSV (the delimiter colored only), Lua, R, Visual Basic, F#.

Each one covers comments, strings, numbers, keywords and types, plus what is specific to the language (preprocessor lines, attributes, tags, keys, headings, `+`/`-` lines). There are two palettes, one per theme, both readable on that theme's background.

## Where the code goes

- `Lignum.Core/Syntax`: the scheme model, the extension lookup (with the `.txt/.log/.lm` exclusion) and the line tokenizer. Colors stay as strings, with no WPF. Unit tests go here.
- `Lignum.App`: the settings file, the built-in schemes, the LogView drawing (`FormattedText.SetForegroundBrush`/`SetFontWeight` per token), the schemes window, and view-model tests.
- Update `docs/design.md` (Highlighting) and `GLOSSARY.md` (syntax scheme, token rule).

## Done when

- [ ] Opening `.cs`, `.json`, `.xml`, `.py` and `.ps1` files colors them, and `.log`, `.txt` and `.lm` files look exactly as before
- [ ] Unknown extensions fall back to the whole-line rules
- [ ] The schemes page adds, edits, deletes, imports, exports and restores factory defaults, and every change is saved
- [ ] A bad or slow regex never blocks the UI or crashes
- [ ] Core tokenizer tests (earliest match wins, ties go by order, timeout, cut-off) and view-model tests (lookup, exclusions, import/export round-trip, factory reset)
