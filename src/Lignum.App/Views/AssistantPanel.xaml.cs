using System.Windows;
using System.Windows.Controls;
using Lignum.App.Assistant;

namespace Lignum.App.Views;

/// <summary>Collapsed for true, visible for false.</summary>
public sealed class InverseBoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The AI access side panel (ticket 05).</summary>
public partial class AssistantPanel : UserControl
{
    private bool _stickToBottom = true;

    public AssistantPanel()
    {
        Resources.Add("InverseBoolToVisibility", new InverseBoolToVisibilityConverter());
        InitializeComponent();
    }

    /// <summary>Copies the MCP setting for the AI tool named in the button's Tag.</summary>
    private void OnCopySetupClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AssistantViewModel assistant || sender is not Button { Tag: string tool }) return;
        try
        {
            Clipboard.SetText(assistant.SetupSnippet(tool));
            CopiedText.Text = "Copied. Paste it into your AI tool's MCP settings.";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            CopiedText.Text = "The clipboard is busy; try again.";
        }
    }

    /// <summary>Enter sends; Shift+Enter adds a line.</summary>
    private void OnInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter
            || System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) return;
        e.Handled = true;
        if (DataContext is AssistantViewModel assistant && assistant.SendCommand.CanExecute(null))
            _ = assistant.SendCommand.ExecuteAsync(null);
    }

    /// <summary>Keeps the newest entry in view, unless the user scrolled up.</summary>
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0)
            _stickToBottom = Scroller.VerticalOffset >= Scroller.ScrollableHeight - 2;
        else if (_stickToBottom)
            Scroller.ScrollToEnd();
    }
}
