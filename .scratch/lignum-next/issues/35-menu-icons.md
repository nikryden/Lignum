# 35: Icons on menu items

**What to build:** Menu items show icons, in the same Segoe Fluent Icons style as the toolbar.

**Blocked by:** 30 (the menu changes)

**Status:** ready-for-agent

## Behaviour

- Icons on the main File, Edit, Go, Search and View items: Open, Open folder, Recent, Export, Open in editor, Copy path, Open containing folder, Close tab, Exit, Copy, Select all, Copy to search, Go to line, Start/End, bookmark items, Find, Find in all tabs/files, Find next/prev, Filter, Results pane, Theme, Settings, Zoom in/out/reset, Detail pane, AI access. The tab and line context menus get them too.
- Where an item has a toolbar button, the menu uses the same glyph.
- Checkable items keep their check mark. Icons use the theme foreground and dim when the item is disabled.

## Design

- An attached property `MenuIcon.Glyph` (string), plus a shared `MenuItem` style that builds a `TextBlock` icon with the Fluent font. This avoids a `TextBlock` per item in XAML.
