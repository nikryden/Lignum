# 33: Distinct background for "You" chat messages

**What to build:** In the AI pane, the user's own messages get a slightly different background, so the two sides of a conversation are easy to tell apart.

**Blocked by:** none

**Status:** ready-for-agent

## Behaviour

- Dark theme: "You" entries are slightly lighter than the pane background (e.g. pane `#1E1E1E` → `#2A2D33`).
- Light theme: a soft tint slightly darker than the white pane (e.g. `#EEF2F8`), since "lighter" is not visible on white.
- Rounded corners and padding. AI replies and tool-call rows are unchanged. Text contrast meets WCAG AA in both themes.

## Design

- New brush `ChatUserMessageBackgroundBrush` in `Themes/Dark.xaml` and `Themes/Light.xaml`, used via `DynamicResource` in the `AssistantPanel` entry template (selected on the entry's role).
