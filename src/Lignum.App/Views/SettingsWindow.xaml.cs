using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Lignum.App.Views;

/// <summary>The pages of the Settings window (ticket 30), in list order.</summary>
public enum SettingsPage
{
    Appearance,
    HighlightRules,
    SyntaxSchemes,
    ExternalEditor,
    Ai,
}

/// <summary>
/// One modeless Settings window (ticket 30). Pages are built on first view and kept while the window is open, so
/// unsaved fields survive switching pages; each page applies its own changes.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>One entry in the page list; Create builds the page, or returns null when it is not available.</summary>
    public sealed record Page(SettingsPage Id, string Title, Func<UIElement?> Create);

    /// <summary>The page shown last, opened again when Settings opens without a page (kept for the app's run).</summary>
    public static SettingsPage LastPage { get; private set; } = SettingsPage.Appearance;

    private readonly Dictionary<SettingsPage, UIElement?> _built = [];

    public SettingsWindow(IEnumerable<Page> pages)
    {
        InitializeComponent();
        PageList.ItemsSource = pages.ToList();
    }

    /// <summary>Switches to <paramref name="page"/> (or the last page shown).</summary>
    public void ShowPage(SettingsPage? page = null)
    {
        var id = page ?? LastPage;
        PageList.SelectedItem = PageList.Items.Cast<Page>().FirstOrDefault(p => p.Id == id) ?? PageList.Items[0];
    }

    private void OnPageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PageList.SelectedItem is not Page page) return;
        LastPage = page.Id;
        if (!_built.TryGetValue(page.Id, out var content)) _built[page.Id] = content = page.Create();
        PageHost.Content = content ?? new TextBlock { Text = "Not available.", Margin = new Thickness(12), Opacity = 0.7 };
    }

    private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e) => Close();
}
