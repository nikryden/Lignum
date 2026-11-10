using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lignum.App.ViewModels;

/// <summary>
/// The tabs of one window (ticket 28): the main window has one group, each floating window another. The
/// group owns the order of its tabs and which one is shown; <see cref="LogTabViewModel.IsSelected"/>
/// means "shown in its window". <see cref="MainViewModel"/> moves tabs between groups.
/// </summary>
public sealed partial class TabGroupViewModel : ObservableObject
{
    internal TabGroupViewModel(bool isMain) => IsMain = isMain;

    /// <summary>True for the main window's group, which stays when it has no tabs.</summary>
    public bool IsMain { get; }

    public ObservableCollection<LogTabViewModel> Tabs { get; } = [];

    /// <summary>A floating window's normal position and size, kept up to date by the window (session restore).</summary>
    public Settings.WindowBounds? Bounds { get; set; }

    /// <summary>Whether the floating window is maximized (session restore).</summary>
    public bool IsMaximized { get; set; }

    /// <summary>The tab shown in this window; null only when the group is empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private LogTabViewModel? _selectedTab;

    /// <summary>The window's title: the file it shows.</summary>
    public string Title => TitleFor(SelectedTab);

    internal static string TitleFor(LogTabViewModel? tab) => tab is null ? "Lignum" : $"{tab.FileName} - Lignum";

    partial void OnSelectedTabChanged(LogTabViewModel? oldValue, LogTabViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    /// <summary>Appends <paramref name="tab"/>; it is shown only when the group showed nothing.</summary>
    internal void Add(LogTabViewModel tab)
    {
        Tabs.Add(tab);
        SelectedTab ??= tab;
    }

    /// <summary>Puts <paramref name="tab"/> at <paramref name="index"/> (clamped) and shows it.</summary>
    internal void Insert(int index, LogTabViewModel tab)
    {
        Tabs.Insert(Math.Clamp(index, 0, Tabs.Count), tab);
        SelectedTab = tab;
    }

    /// <summary>Moves <paramref name="tab"/> (already in this group) to <paramref name="index"/> (clamped).</summary>
    internal void Reorder(LogTabViewModel tab, int index)
    {
        var from = Tabs.IndexOf(tab);
        var to = Math.Clamp(index, 0, Tabs.Count - 1);
        if (from >= 0 && from != to) Tabs.Move(from, to);
    }

    /// <summary>
    /// Takes <paramref name="tab"/> out of the group. When it was shown, the tab that takes its place is
    /// shown instead, or the one before it.
    /// </summary>
    internal void Remove(LogTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        if (ReferenceEquals(tab, SelectedTab))
        {
            SelectedTab = Tabs.Count > 1 ? Tabs[index + 1 < Tabs.Count ? index + 1 : index - 1] : null;
        }
        Tabs.RemoveAt(index);
    }
}
