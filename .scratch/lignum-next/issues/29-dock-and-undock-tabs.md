# 29: Dock and undock tabs by dragging

**What to build:** Tabs can be dragged out of the main window into their own window, and dragged back in. A floating window has its own tab strip and can hold several tabs. Tabs can be dragged between any windows and reordered inside a strip. Session restore reopens floating windows where they were.

**Blocked by:** none (builds on 16 tabs, 18 session restore, lignum-v1)

**Status:** ready-for-agent

## Behaviour

- **Reorder:** drag a tab left or right in a strip. A marker shows where it will land.
- **Undock:** drag a tab and release it outside every tab strip. A new floating window opens under the cursor and holds that tab.
- **Move / dock:** release a dragged tab on any window's tab strip, the main window's or a floating one's. The tab moves there at the marker.
- A drag only starts after the mouse moves past the system drag distance, so clicking a tab and its ✕ button work as before. Dragging files from Explorer onto a window still opens them (ticket 16).
- **Floating window:** a tab strip, the tab's view (find bar, filter bar, detail pane, change banner, the same as in the main window) and a status bar. There is no toolbar or menu. The title is the selected file name. It shows in the taskbar and is not owned by the main window, so it can go behind it.
- **Active tab:** the main window's toolbar, menus, Find window, all-tabs search and AI tools act on the *active tab*, which is the selected tab of the window activated last. Keyboard shortcuts (Ctrl+F, Ctrl+W, Ctrl+G, F3 and the rest) work in floating windows too.
- **New files** (open, drop, recent files) open in the active window.
- **Menu fallback:** the tab right-click menu has **Move to new window** and **Move to main window**. There is also **Move to window ▸**, which lists the other windows. Items that would do nothing are hidden.
- **Emptying a window:** when a floating window's last tab is moved or closed, the window closes. The main window stays open with no tabs, as it does today.
- **Closing a floating window** (its ✕ or Alt+F4) moves its tabs back to the main window, so no index is lost by accident. (Decided: dock back, not close as browsers do.)
- **Closing the main window** saves the session, then closes every floating window and the app exits.
- **Session restore:** each floating window's tabs, order, selected tab, position, size and maximized state are restored. A window that would land off every current screen is moved onto the primary screen.
- The hard constraints still hold. Moving a tab never re-indexes or reopens its file.

## Design

- New `TabGroupViewModel` (one per window): `Tabs` (ordered) and `SelectedTab`. `LogTabViewModel.IsSelected` means "visible in its own window" and is driven by its group.
- `MainViewModel` keeps `Tabs` as the list of every open tab in every window, so all-tabs search, the AI tools, the find window and close-all stay as they are. It adds `MainGroup`, `FloatingGroups`, and `MoveTab(tab, targetGroup, index)` / `MoveToNewWindow(tab)`.
- `MainViewModel.SelectedTab` becomes the active tab: it is set when a window activates or its group's selection changes. The main window's strip, content and status bar bind to `MainGroup` instead. Each of the 61 `SelectedTab` bindings in `MainWindow.xaml` is checked to decide whether it means "active tab" (toolbar, menus) or "main window's tab" (strip, status bar, title).
- A new `TabWindow` (floating) reuses one shared tab-strip control and `LogTabView`. The tab strip `DataTemplate` moves out of `MainWindow.xaml` into a shared resource or `UserControl`, so all windows use the same template.
- Drag is a custom gesture with mouse capture, not OLE `DoDragDrop`. A drop outside every window has to be detected, and OLE drag would collide with the existing file-drop handling. While dragging, each window's strip is hit-tested with `PointFromScreen` and a small ghost popup follows the cursor.
- A moved tab gets a new `LogTabView` in its new window. Any view-only state (scroll offset, horizontal offset, caret, selection) must survive. It is checked in step 1 and moved into the view model if it doesn't.
- Window input bindings are defined once and attached to both window types.
- `SessionSettings` gains `Windows: [{ Left, Top, Width, Height, Maximized, SelectedIndex }]` and `SessionTab.Window` (0 = main). An old `settings.json` with no `Windows` restores everything into the main window.

## Steps (rough token estimates)

1. **Model** (~60k): `TabGroupViewModel` and the move/reorder/new-window/close-window logic in `MainViewModel`, with active-tab tracking. Check that moving a tab keeps its view state. VM tests: reorder, move between groups, last tab out closes the group, closing a group docks its tabs, active tab follows activation, close tab in a floating group.
2. **Windows and menus** (~90k): extract the tab strip, add `TabWindow`, rebind `MainWindow` to `MainGroup`, share input bindings, add the right-click menu items, title, and app shutdown order.
3. **Drag** (~80k): drag threshold, reorder marker, drop on another window's strip, drop outside opens a window at the cursor, ghost popup. File drop must still work.
4. **Session** (~40k): save and restore windows and tab placement, clamp windows to the screens, load old settings without `Windows`. VM tests.
5. **Manual test and fixes** (~40k): multi-monitor, a 2 GB file moved while indexing, a search running while its tab moves, Find window, AI `list_tabs` (show the window).

Total: about 310k tokens.

## Done when

- [x] Tabs reorder by drag inside a strip
- [x] Dragging a tab outside every strip opens it in a new floating window; dragging it onto a strip moves it there
- [x] Floating windows hold several tabs, and tabs move freely between all windows
- [ ] Moving a tab keeps its index, scroll position, selection, search results and filters (no re-index)
- [x] Toolbar, menus, shortcuts, Find window and AI tools act on the active tab in whichever window is active
- [x] Right-click: Move to new window / Move to main window / Move to window ▸
- [x] A floating window closes when emptied; closing it docks its tabs back
- [x] Session restore brings back floating windows, their tabs, positions and sizes; old settings still load
- [x] VM tests for groups, moves, active tab and session

## Comments

Step 1 (model): `TabGroupViewModel` (`Tabs`, `SelectedTab`, `Title`; it drives `LogTabViewModel.IsSelected`). `MainViewModel.Windows.cs` adds `MainGroup`, `FloatingGroups`, `ActiveGroup`, `ActivateGroup`, `GroupOf`, `MoveTab(tab, group, index)` (it reorders when the tab is already in that group), `MoveToNewGroup` with `MoveToNewWindowCommand`, `MoveToMainWindowCommand` and `CloseFloatingGroup` (docks the tabs back in order). `Tabs` still holds every tab. `SelectedTab` is the active tab. Setting it shows the tab in its group and makes that group active. A selection change in the active group updates it. New tabs go to `ActiveGroup`. A floating group left empty is removed, and the main group becomes active. The view keeps caret and selection in the view model already (`Navigation.Caret`, `Selection`). The scroll line is restored with `RestoreScroll` on each move. The horizontal scroll offset is view-only and resets on a move; step 2 will decide if that matters. Tests: `TabWindowsTests`.

Notes for step 2: `LogTabView` binds `Highlighting`, `Syntax` and `Appearance` through `AncestorType=Window`, and `OnFullLineRequested` casts `Window.DataContext` to `MainViewModel`. So the floating window's DataContext should be the `MainViewModel`, with its strip bound to its group. The main window's strip, content `ItemsControl`, status bar and title must bind to `MainGroup`, not `Tabs` and `SelectedTab`.

Step 2 (windows and menus): the tab strip and status bar are now shared controls, `Views/TabStrip` and `Views/TabStatusBar`. Their DataContext is a `TabGroupViewModel`, so `SelectedTab.*` means the tab shown in that window, and app-wide items (package status, the open error) come from the window's `MainViewModel`. `MainWindow` binds its strip, status bar and content to `MainGroup`. Its title is `MainGroup`'s file. The menus and key bindings still use `SelectedTab`, which is the active tab. `Views/TabWindow` is the floating window: DataContext is `MainViewModel`, `Group` is its tabs, and it has the same `Appearance`/`Highlighting`/`Syntax` properties that `LogTabView` binds through `AncestorType=Window`. It also handles file drops (they open in that window) and Ctrl+wheel zoom. `MainWindow.TabWindows.cs` opens a `TabWindow` per floating group (under the mouse, 70% of the main window's size) and closes it when the group goes. Activation sets the active group. When the active group changes (a move, a search result), its window comes to the front. Keys not handled in a floating window run the main window's `InputBindings`, then its routed-command gestures (Ctrl+O). Go to line and the Find window are owned by the active tab's window. The Find window goes back to the main window before a floating one closes, because owned windows close with their owner. On exit, the session is saved and then the floating windows close without docking. The right-click menu has Move to new window, Move to main window (hidden when they would do nothing), and Move to window ▸, which lists the other floating windows (hidden when there are none). The horizontal scroll offset still resets on a move. Checked: build, tests, and the app starts. The floating window is not yet checked by hand.

Step 3 (drag): `TabStrip.Drag.cs`. Pressing on a tab (not its ✕) and moving past the system drag distance makes the strip capture the mouse. A ghost popup with the file name follows the pointer, offset so it is never the window under the pointer. The drop target is the top window under the pointer (`WindowFromPoint`/`GetAncestor`), matched against every loaded `TabStrip`. When the pointer is over that window's tab row (with a few pixels of slack), a marker shows the insertion point. A window with no tabs (an empty main window) takes the drop anywhere. Releasing over a strip calls `MoveTab`; inside its own strip it is a reorder, using `TabDropMath.ReorderIndex`. Releasing elsewhere calls `MoveToNewGroup`, and the window opens under the pointer. A tab alone in a floating window moves that window instead, and the ghost says "(move window)". Esc or losing the capture cancels. Files dragged from Explorer still use the OLE drop, which is untouched. The insert-index and marker maths are in `TabDropMath`. Tests: `TabDropMathTests`. Checked: build, tests, and the app starts. Not yet checked by hand.

Step 4 (session): `SessionSettings.Windows` (one `SessionWindow` per floating window: `Bounds`, `Maximized`, `SelectedIndex`) and `SessionTab.Window` (0 = main, n = the n-th window). Tabs are saved grouped by window, in strip order. `SelectedIndex` is now the main window's shown tab. `TabWindow` keeps `TabGroupViewModel.Bounds` (normal bounds, also when maximized) and `IsMaximized` up to date. On restore, a floating group is created for a window when its first tab opens. A window whose files are all gone is not reopened. An unknown window number, or an older file with no `windows`, opens the tab in the main window. The main window ends active. A restored window opens at its bounds, without taking the focus. If its title bar would be off the virtual screen, it is centred on the primary work area. That check uses the virtual screen's bounding box, so a gap in an L-shaped monitor layout is not caught. Tests: three in `SessionTests`. Checked by launching with a saved floating window in a temp settings folder: both windows open, the floating one at its saved place with its tab.
