# 13: Settings, themes, font and zoom

**What to build:** A JSON settings store under the roaming app data `Lignum` folder (no registry), with a location that tests can redirect. Light and dark themes follow the Windows setting by default, and the user's choice is remembered. The user can change the monospaced font, zoom with Ctrl+mouse wheel and set the tab width (default 4), all remembered between runs. All brushes come from theme resources so later features inherit theming.

**Blocked by:** 03

**Status:** resolved

- [x] Settings are saved and loaded from JSON; tests use a temp folder
- [x] The theme follows Windows by default; the user's choice persists across restarts
- [x] Font, zoom and tab width changes apply immediately and persist
- [x] Viewer, panes and status bar look correct in both themes
- [x] A missing or corrupt settings file falls back to defaults and is logged

## Comments

`SettingsStore` (injectable folder, default `%APPDATA%\Lignum`) + `SettingsFile<T>` load/save any JSON file there; missing/corrupt files give defaults and are logged, unknown fields survive a save. `AppearanceViewModel` owns theme (System/Light/Dark, follows Windows live), font, zoom (Ctrl+wheel, Ctrl+Plus/Minus/0) and tab width, saving on every change. `ThemeManager` applies WPF Fluent `ThemeMode` plus `Themes/Light.xaml`/`Dark.xaml` brushes (keys in `ThemeKeys`).
Visual check: smoke-tested the window (viewer, menu, status bar) in both themes with zoom 120 and tab width 8 via screenshots. Panes other than the file-changed banner don't exist yet; they should use the `ThemeKeys` brushes.
