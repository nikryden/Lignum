# 34: Resizable find-in-files results pane

**What to build:** The find-in-all-tabs / find-in-files results pane can be resized by dragging its top edge.

**Blocked by:** none

**Status:** ready-for-agent

## Behaviour

- A horizontal splitter sits on the pane's top edge. Height is limited to between about 80 px and 80 % of the window.
- The height is saved in settings and restored on the next start. The default stays 260.
- Double-clicking the splitter resets to the default.

## Design

- Replace the fixed `Height="260"` Border in `MainWindow.xaml` with a row layout + `GridSplitter`, or reuse `DetailPaneLayout` with `Dock=Bottom`, as the AI pane does. Persist `AllTabsSearch.PaneHeight` in `AppSettings`.
