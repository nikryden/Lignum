# 13: Settings, themes, font and zoom

**What to build:** A JSON settings store under the roaming app data `Lignum` folder (no registry), with a location that tests can redirect. Light and dark themes follow the Windows setting by default, and the user's choice is remembered. The user can change the monospaced font, zoom with Ctrl+mouse wheel and set the tab width (default 4), all remembered between runs. All brushes come from theme resources so later features inherit theming.

**Blocked by:** 03

**Status:** ready-for-agent

- [ ] Settings are saved and loaded from JSON; tests use a temp folder
- [ ] The theme follows Windows by default; the user's choice persists across restarts
- [ ] Font, zoom and tab width changes apply immediately and persist
- [ ] Viewer, panes and status bar look correct in both themes
- [ ] A missing or corrupt settings file falls back to defaults and is logged
