# 04: Scroll the whole file

**What to build:** The user can scroll anywhere in a multi-GB log smoothly. The scrollbar maps to line numbers across the whole file and grows as indexing continues. The mouse wheel, arrow keys, PageUp/PageDown and dragging the scrollbar thumb all work. There is no word wrap; long lines scroll horizontally.

**Blocked by:** 03

**Status:** ready-for-agent

- [ ] The vertical scrollbar range equals the indexed line count and updates while indexing runs, without the view jumping
- [ ] Mouse wheel, arrow keys and PageUp/PageDown scroll correctly
- [ ] Dragging the thumb to the middle or end of a 2 GB file renders without noticeable lag
- [ ] Horizontal scrolling for lines wider than the view
- [ ] The math that maps pixels to line and column (and back) is a pure helper with unit tests
