using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lignum.App.ViewModels;

// Docking and undocking tabs (ticket 29): the main window's tabs are MainGroup, each floating window's
// are one of FloatingGroups. Tabs keeps every tab. SelectedTab is the active tab: the tab shown in the
// window that was activated last, which the main window's toolbar, menus and shortcuts act on.
// Moving a tab never reopens or re-indexes its file; the new window builds a new view for it.
public sealed partial class MainViewModel
{
    /// <summary>The main window's tabs.</summary>
    public TabGroupViewModel MainGroup { get; }

    /// <summary>One group per floating window; the view opens a window when one is added and closes it when removed.</summary>
    public ObservableCollection<TabGroupViewModel> FloatingGroups { get; } = [];

    /// <summary>The group of the window activated last; new files open in it.</summary>
    [ObservableProperty]
    private TabGroupViewModel _activeGroup = null!;

    /// <summary>The window of <paramref name="group"/> was activated: its shown tab becomes the active tab.</summary>
    public void ActivateGroup(TabGroupViewModel group)
    {
        // A new floating window shows (and activates) before its first tab arrives: wait for MoveTab.
        if (!IsOpenGroup(group) || (!group.IsMain && group.Tabs.Count == 0)) return;
        ActiveGroup = group;
        SelectedTab = group.SelectedTab;
    }

    /// <summary>The group that holds <paramref name="tab"/>; null when the tab is not open.</summary>
    public TabGroupViewModel? GroupOf(LogTabViewModel tab) =>
        MainGroup.Tabs.Contains(tab) ? MainGroup : FloatingGroups.FirstOrDefault(g => g.Tabs.Contains(tab));

    /// <summary>
    /// Moves <paramref name="tab"/> to <paramref name="index"/> (clamped) in <paramref name="target"/>:
    /// a reorder when it is already there. The tab is shown there and becomes the active tab. A floating
    /// window left with no tabs closes.
    /// </summary>
    public void MoveTab(LogTabViewModel tab, TabGroupViewModel target, int index)
    {
        if (GroupOf(tab) is not { } source || !IsOpenGroup(target)) return;
        if (ReferenceEquals(source, target))
        {
            target.Reorder(tab, index);
            SelectedTab = tab;
            return;
        }

        TakeOut(tab);
        target.Insert(index, tab);
        tab.RestoreScroll(tab.ScrollLine); // the new window's view starts at the top: scroll it back
        SelectedTab = tab;
        NotifyMoveCommands();
    }

    /// <summary>Right-click "Move to new window": moves the tab (null = the active tab) into a new floating window.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveToNewWindow))]
    private void MoveToNewWindow(LogTabViewModel? tab) => MoveToNewGroup(tab);

    /// <summary>Moves <paramref name="tab"/> (null = the active tab) into a new floating window and returns its group (null: not moved).</summary>
    public TabGroupViewModel? MoveToNewGroup(LogTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null || !CanMoveToNewWindow(tab)) return null;

        var group = new TabGroupViewModel(isMain: false);
        Watch(group);
        FloatingGroups.Add(group);
        MoveTab(tab, group, 0);
        return group;
    }

    /// <summary>A tab alone in a floating window is already in its own window.</summary>
    private bool CanMoveToNewWindow(LogTabViewModel? tab) =>
        (tab ?? SelectedTab) is { } t && GroupOf(t) is { } group && (group.IsMain || group.Tabs.Count > 1);

    /// <summary>Docks <paramref name="tab"/>: moves it to the end of the main window's tabs.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveToMainWindow))]
    private void MoveToMainWindow(LogTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is not null && CanMoveToMainWindow(tab)) MoveTab(tab, MainGroup, MainGroup.Tabs.Count);
    }

    private bool CanMoveToMainWindow(LogTabViewModel? tab) =>
        (tab ?? SelectedTab) is { } t && GroupOf(t) is { IsMain: false };

    /// <summary>
    /// A floating window was closed: its tabs move to the end of the main window's tabs, in order, so no
    /// file is closed by accident. The tab it showed is shown in the main window.
    /// </summary>
    public void CloseFloatingGroup(TabGroupViewModel group)
    {
        if (group.IsMain || !FloatingGroups.Contains(group)) return;

        var shown = group.SelectedTab;
        foreach (var tab in group.Tabs.ToList())
        {
            group.Remove(tab);
            MainGroup.Insert(MainGroup.Tabs.Count, tab);
            tab.RestoreScroll(tab.ScrollLine);
        }
        if (shown is not null) MainGroup.SelectedTab = shown;
        RemoveFloatingGroup(group);
    }

    /// <summary>Takes <paramref name="tab"/> out of its group; a floating group left empty is removed.</summary>
    private void TakeOut(LogTabViewModel tab)
    {
        if (GroupOf(tab) is not { } group) return;
        group.Remove(tab);
        if (!group.IsMain && group.Tabs.Count == 0) RemoveFloatingGroup(group);
    }

    private void RemoveFloatingGroup(TabGroupViewModel group)
    {
        group.PropertyChanged -= OnGroupPropertyChanged;
        FloatingGroups.Remove(group);
        if (ReferenceEquals(ActiveGroup, group)) ActivateGroup(MainGroup);
        NotifyMoveCommands();
    }

    private void Watch(TabGroupViewModel group) => group.PropertyChanged += OnGroupPropertyChanged;

    /// <summary>A tab shown in the active window (e.g. clicked in its strip, or after a close) is the active tab.</summary>
    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TabGroupViewModel.SelectedTab)) return;
        if (ReferenceEquals(sender, MainGroup)) OnPropertyChanged(nameof(Title));
        if (ReferenceEquals(sender, ActiveGroup)) SelectedTab = ActiveGroup.SelectedTab;
    }

    private bool IsOpenGroup(TabGroupViewModel group) => group.IsMain ? ReferenceEquals(group, MainGroup) : FloatingGroups.Contains(group);

    private void NotifyMoveCommands()
    {
        MoveToNewWindowCommand.NotifyCanExecuteChanged();
        MoveToMainWindowCommand.NotifyCanExecuteChanged();
    }
}
