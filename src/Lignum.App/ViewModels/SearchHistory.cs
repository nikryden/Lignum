using System.Collections.ObjectModel;
using Lignum.App.Settings;

namespace Lignum.App.ViewModels;

/// <summary>
/// The app-wide find and filter history shown in the find box and filter add box dropdowns: the last
/// <see cref="MaxEntries"/> terms each, most recent first, no duplicates (case-sensitive: "Error" and
/// "error" are different searches). Kept in settings.json and saved on every change. UI thread only.
/// </summary>
public sealed class SearchHistory
{
    public const int MaxEntries = 20;

    private readonly SettingsFile<AppSettings> _settings;

    public SearchHistory(SettingsFile<AppSettings> settings)
    {
        _settings = settings;
        Load(SearchTerms, settings.Value.SearchHistory);
        Load(FilterTerms, settings.Value.FilterHistory);
    }

    /// <summary>Find box terms, most recent first.</summary>
    public ObservableCollection<string> SearchTerms { get; } = [];

    /// <summary>Filter rule texts, most recent first.</summary>
    public ObservableCollection<string> FilterTerms { get; } = [];

    public void AddSearchTerm(string term)
    {
        if (Add(SearchTerms, term)) Save();
    }

    public void AddFilterTerm(string term)
    {
        if (Add(FilterTerms, term)) Save();
    }

    private static void Load(ObservableCollection<string> target, List<string>? saved)
    {
        foreach (var term in saved ?? [])
        {
            if (string.IsNullOrEmpty(term) || target.Contains(term)) continue;
            target.Add(term);
            if (target.Count == MaxEntries) break;
        }
    }

    /// <summary>Moves (or adds) <paramref name="term"/> to the top; false when nothing changed.</summary>
    private static bool Add(ObservableCollection<string> list, string term)
    {
        if (string.IsNullOrEmpty(term)) return false;
        var existing = list.IndexOf(term);
        if (existing == 0) return false;
        if (existing > 0) list.Move(existing, 0);
        else list.Insert(0, term);
        while (list.Count > MaxEntries) list.RemoveAt(list.Count - 1);
        return true;
    }

    private void Save()
    {
        _settings.Value.SearchHistory = [.. SearchTerms];
        _settings.Value.FilterHistory = [.. FilterTerms];
        _settings.Save();
    }
}
