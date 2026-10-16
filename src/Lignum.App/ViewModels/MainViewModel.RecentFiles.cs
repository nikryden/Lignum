using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Lignum.App.ViewModels;

// File > Open recent. The list is app-wide and lives in settings.json (AppSettings.RecentFiles).
public sealed partial class MainViewModel
{
    public const int MaxRecentFiles = 10;

    /// <summary>Full paths, most recently opened first, at most <see cref="MaxRecentFiles"/>.</summary>
    public ObservableCollection<string> RecentFiles { get; } = [];

    public bool HasRecentFiles => RecentFiles.Count > 0;

    /// <summary>
    /// Opens a file from the recent list. A file that no longer exists is not opened: it is removed from
    /// the list and the user is told why.
    /// </summary>
    [RelayCommand]
    private Task OpenRecent(string path)
    {
        if (!File.Exists(path))
        {
            RemoveRecentFile(path);
            ErrorMessage = $"{path} no longer exists and was removed from the recent files.";
            return Task.CompletedTask;
        }

        return OpenFileAsync(path);
    }

    private void LoadRecentFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _settings.Value.RecentFiles)
        {
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path)) continue;
            RecentFiles.Add(path);
            if (RecentFiles.Count == MaxRecentFiles) break;
        }
        RecentFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentFiles));
    }

    /// <summary>Moves (or adds) <paramref name="fullPath"/> to the top of the list and saves it.</summary>
    private void AddRecentFile(string fullPath)
    {
        var existing = IndexOfRecent(fullPath);
        if (existing == 0) return;
        if (existing > 0) RecentFiles.RemoveAt(existing);
        RecentFiles.Insert(0, fullPath);
        while (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveAt(RecentFiles.Count - 1);
        SaveRecentFiles();
    }

    private void RemoveRecentFile(string path)
    {
        var index = IndexOfRecent(path);
        if (index < 0) return;
        RecentFiles.RemoveAt(index);
        SaveRecentFiles();
    }

    private int IndexOfRecent(string path)
    {
        for (var i = 0; i < RecentFiles.Count; i++)
        {
            if (string.Equals(RecentFiles[i], path, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private void SaveRecentFiles()
    {
        _settings.Value.RecentFiles = [.. RecentFiles];
        _settings.Save();
    }
}
