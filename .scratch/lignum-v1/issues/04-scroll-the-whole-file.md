# 04: Scroll the whole file

**What to build:** The user can scroll anywhere in a multi-GB log smoothly. The scrollbar maps to line numbers across the whole file and grows as indexing continues. The mouse wheel, arrow keys, PageUp/PageDown and dragging the scrollbar thumb all work. There is no word wrap; long lines scroll horizontally.

**Blocked by:** 03

**Status:** resolved

- [x] The vertical scrollbar range equals the indexed line count and updates while indexing runs, without the view jumping
- [x] Mouse wheel, arrow keys and PageUp/PageDown scroll correctly
- [x] Dragging the thumb to the middle or end of a 2 GB file renders without noticeable lag
- [x] Horizontal scrolling for lines wider than the view
- [x] The math that maps pixels to line and column (and back) is a pure helper with unit tests

## Comments

`LogView` is now its own `IScrollInfo` (rows and columns are the scroll units), hosted in a `ScrollViewer` with `CanContentScroll="True"`. Wheel (touchpad remainders carried, Shift = horizontal), arrows and PageUp/PageDown are handled in `Viewer/LogView.Scrolling.cs`. Pure math in `ViewportMath`, `ViewportGeometry` (pixel <-> row/column) and `WheelAccumulator`, covered by `ViewportMathTests`.
Manual smoke test on a 0.93 GB / 10.5M-line file (not 2 GB): jumping the scrollbar to 50% and 100% through UI Automation rendered at once, and keys, wheel and horizontal scrolling all behaved correctly. No one dragged the thumb with a physical mouse; UIA `SetScrollPercent` goes through the same `SetVerticalOffset` path.
