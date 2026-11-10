using System.Windows;
using System.Windows.Controls;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

/// <summary>One window's tab strip (ticket 28); DataContext is the window's <see cref="TabGroupViewModel"/>.</summary>
public partial class TabStrip : UserControl
{
    public TabStrip()
    {
        InitializeComponent();
        InitializeDrag();
    }

    /// <summary>
    /// Fills "Move to window" with the other floating windows. It is hidden when there are none (the main
    /// window has its own item).
    /// </summary>
    private void OnTabMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.DataContext is not LogTabViewModel tab
            || Window.GetWindow(this)?.DataContext is not MainViewModel main) return;
        var item = menu.Items.OfType<MenuItem>().FirstOrDefault(i => "MoveToWindow".Equals(i.Tag));
        if (item is null) return;

        var own = main.GroupOf(tab);
        var targets = main.FloatingGroups.Where(g => !ReferenceEquals(g, own)).ToList();
        item.Items.Clear();
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var entry = new MenuItem { Header = WindowName(target) };
            entry.Click += (_, _) => main.MoveTab(tab, target, target.Tabs.Count);
            item.Items.Add(entry);
        }
        item.Visibility = targets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A floating window as the menu names it: its shown file, and how many more tabs it has.</summary>
    private static string WindowName(TabGroupViewModel group)
    {
        var name = group.SelectedTab?.FileName ?? "Window";
        name = name.Replace("_", "__"); // not an access key
        return group.Tabs.Count > 1 ? $"{name} (+{group.Tabs.Count - 1})" : name;
    }
}
