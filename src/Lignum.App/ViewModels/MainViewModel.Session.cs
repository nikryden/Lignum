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
        _settings.Value.Session = new SessionSettings
        {
            Tabs = [.. Tabs.Select(tab => new SessionTab
            {
                Path = tab.Path,
                FirstLine = tab.ScrollLine,
                FilterRules = [.. tab.Search.GetFilterRules()],
            })],
            SelectedIndex = SelectedTab is { } selected ? Tabs.IndexOf(selected) : -1,
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

            var document = File.Exists(fullPath) ? await OpenDocumentAsync(fullPath, entry.Path) : null;
            if (document is null)
            {
                skipped.Add(entry.Path);
                continue;
            }

            if (FindTab(fullPath) is { } opened) // opened from the command line meanwhile
            {
                document.Dispose();
                if (i == session.SelectedIndex) selected = opened;
                continue;
            }

            var tab = AddTab(document);

            tab.RestoreScroll(entry.FirstLine);
            if (entry.FilterRules is { Count: > 0 } rules) tab.Search.SetFilterRules(rules);
            indexing.Add(tab.Start()); // the rules run once the document is attached
            if (i == session.SelectedIndex) selected = tab;
        }

        SelectedTab = selected ?? SelectedTab ?? Tabs.LastOrDefault();
        ErrorMessage = skipped.Count switch
        {
            0 => ErrorMessage,
            1 => $"Could not reopen {skipped[0]}: the file no longer exists or cannot be opened.",
            _ => $"Could not reopen {skipped.Count} files that no longer exist or cannot be opened: {string.Join(", ", skipped)}",
        };
        await Task.WhenAll(indexing);
    }
}
