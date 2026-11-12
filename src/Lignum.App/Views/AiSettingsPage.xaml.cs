using System.Windows;
using System.Windows.Controls;
using Lignum.App.Assistant;

namespace Lignum.App.Views;

/// <summary>Settings > AI (tickets 30, 31): MCP setup snippets and the AI permissions.</summary>
public partial class AiSettingsPage : UserControl
{
    private readonly AssistantViewModel _assistant;

    public AiSettingsPage(AssistantViewModel assistant)
    {
        _assistant = assistant;
        DataContext = assistant;
        InitializeComponent();
        PermissionsHost.Content = new AiPermissionsPage(assistant.Permissions);
    }

    /// <summary>Copies the MCP setting for the AI tool named in the button's Tag.</summary>
    private void OnCopySetupClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tool }) return;
        try
        {
            Clipboard.SetText(_assistant.SetupSnippet(tool));
            CopiedText.Text = "Copied. Paste it into your AI tool's MCP settings.";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            CopiedText.Text = "The clipboard is busy; try again.";
        }
    }
}
