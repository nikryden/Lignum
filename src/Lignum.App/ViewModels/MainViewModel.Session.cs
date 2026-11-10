using Lignum.App.Settings;

namespace Lignum.App.ViewModels;

// Session restore (ticket 18): the open tabs, their scroll positions and filter rules are saved in
// settings.json on exit and reopened on start. Only the paths and view state are saved; each file is
// re-indexed in the background, exactly as when it is opened by hand.
public sealed partial class MainViewModel
{
    /// <summary>Writes the open tabs to settings.json (the app calls this on exit, before closing them).</summary>
    public void SaveSession()
    {
        // Ticket 29: grouped by window, each window's tabs in its strip order.
        var groups = new List<TabGroupViewModel> { MainGroup };
        groups.AddRange(FloatingGroups.Where(g => g.Tabs.Count > 0));
        var placed = groups.SelectMany((group, window) => group.Tabs.Select(tab => (Tab: tab, Window: window))).ToList();
        var ordered = placed.Select(p => p.Tab).ToList();

        _settings.Value.Session = new SessionSettings
        {
            Tabs = [.. placed.Select(e => new SessionTab
            {
                Path = e.Tab.SourcePath,
                Window = e.Window,
                FirstLine = e.Tab.ScrollLine,
                FilterRules = [.. e.Tab.Search.GetFilterRules()],
                SearchText = e.Tab.Search.ActiveSearchText,
                SearchIsRegex = e.Tab.Search.IsRegex,
                SearchMatchCase = e.Tab.Search.MatchCase,
                SearchNonBacktracking = e.Tab.Search.UseNonBacktracking,
                SearchInsideFilter = e.Tab.Search.SearchInsideFilter,
                HideNonMatchingLines = e.Tab.HideNonMatchingLines,
            })],
            SelectedIndex = MainGroup.SelectedTab is { } selected ? ordered.IndexOf(selected) : -1,
            Windows = [.. groups.Skip(1).Select(group => new SessionWindow
            {
                Bounds = group.Bounds,
                Maximized = group.IsMaximized,
                SelectedIndex = group.SelectedTab is { } shown ? ordered.IndexOf(shown) : -1,
            })],
        };
        _settings.Save();
    }

    /// <summary>
    /// Reopens the tabs saved by <see cref="SaveSession"/>. Returns as soon as the tabs exist; each file is
    /// indexed in the background and the returned task completes when all of them have finished (the app
    /// does not await it). Files that no longer exist (or cannot be opened) are skipped and listed in
    /// <see cref="ErrorMessage"/>. Restoring does not change the recent files.
    /// </summary>
    public async Task RestoreSession()
    {
        var session = _settings.Value.Session ?? new SessionSettings();
        var saved = session.Tabs ?? [];
        var skipped = new List<string>();
        var indexing = new List<Task>();
        LogTabViewModel? selected = null;
        var windows = session.Windows ?? [];
        var floating = new TabGroupViewModel?[windows.Count];

        for (var i = 0; i < saved.Count; i++)
        {
            var entry = saved[i];
            if (entry is null || string.IsNullOrWhiteSpace(entry.Path)) continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(entry.Path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                skipped.Add(entry.Path);
                continue;
            }

            if (FindTab(fullPath) is { } open) // listed twice, or already opened from the command line
            {
                if (i == session.SelectedIndex) selected = open;
                continue;
            }

            // Ticket 29: the tab's floating window, made when its first tab opens; an unknown window is the main one.
            var window = entry.Window >= 1 && entry.Window <= windows.Count ? entry.Window - 1 : -1;
            var group = window < 0 ? MainGroup : floating[window] ??= NewFloatingGroup(windows[window]);
            var added = File.Exists(fullPath) ? await AddTabAsync(fullPath, entry.Path, group) : null;
            if (added is null)
            {
                skipped.Add(entry.Path);
                continue;
            }

            if (!added.Value.IsNew) // opened from the command line meanwhile
            {
                if (i == session.SelectedIndex) selected = added.Value.Tab;
                continue;
            }

            var tab = added.Value.Tab;
            if (!group.IsMain)
            {
                if (i == windows[window].SelectedIndex) group.SelectedTab = tab;
                if (!FloatingGroups.Contains(group)) FloatingGroups.Add(group); // its window opens with this tab
            }

            tab.RestoreScroll(entry.FirstLine);
            if (entry.FilterRules is { Count: > 0 } rules) tab.Search.SetFilterRules(rules);
            tab.Search.SearchInsideFilter = entry.SearchInsideFilter;
            if (!string.IsNullOrEmpty(entry.SearchText))
            {
                tab.Search.RestoreSearch(entry.SearchText, entry.SearchIsRegex, entry.SearchMatchCase,
                    entry.SearchNonBacktracking);
            }

            tab.HideNonMatchingLines = entry.HideNonMatchingLines;
            indexing.Add(tab.Start()); // the rules run once the document is attached
            if (i == session.SelectedIndex) selected = tab;
        }

        SelectedTab = selected ?? MainGroup.SelectedTab ?? MainGroup.Tabs.LastOrDefault();
        if (SelectedTab is null || !MainGroup.Tabs.Contains(SelectedTab)) ActivateFallback();
        ErrorMessage = skipped.Count switch
        {
            0 => ErrorMessage,
            1 => $"Could not reopen {skipped[0]}: the file no longer exists or cannot be opened.",
            _ => $"Could not reopen {skipped.Count} files that no longer exist or cannot be opened: {string.Join(", ", skipped)}",
        };
        await Task.WhenAll(indexing);
    }

    /// <summary>A restored floating window's group, not yet open (it opens with its first tab).</summary>
    private TabGroupViewModel NewFloatingGroup(SessionWindow? saved)
    {
        var group = new TabGroupViewModel(isMain: false) { Bounds = saved?.Bounds, IsMaximized = saved?.Maximized ?? false };
        Watch(group);
        return group;
    }
}
