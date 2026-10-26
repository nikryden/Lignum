using Lignum.App.Settings;

namespace Lignum.App.ViewModels;

/// <summary>
/// The app-wide services <see cref="MainViewModel"/> hands to every tab. Any of them can be null when a tab
/// is used in isolation (tests); the tab then skips that feature.
/// </summary>
/// <param name="Copier">Copies selections (clipboard, prompts).</param>
/// <param name="Highlighting">The app-wide highlight rules ("Add as highlight rule").</param>
/// <param name="History">The app-wide find / filter history.</param>
/// <param name="SettingsStore">Where each tab's bookmarks file lives; null: bookmarks are not saved.</param>
/// <param name="ConfirmClearBookmarks">Asks before clearing bookmarks, given how many have notes; null: no question.</param>
/// <param name="OpenNoteLink">Opens a link in a bookmark note (true: show it in Explorer instead); null: links do nothing.</param>
internal sealed record TabServices(
    SelectionCopier? Copier = null,
    HighlightingViewModel? Highlighting = null,
    SearchHistory? History = null,
    SettingsStore? SettingsStore = null,
    Func<int, bool>? ConfirmClearBookmarks = null,
    Func<NoteLink, bool, Task>? OpenNoteLink = null);
