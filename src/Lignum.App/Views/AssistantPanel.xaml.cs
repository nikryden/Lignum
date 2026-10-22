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

    /// <summary>Enter sends; Shift+Enter adds a line. While skills are suggested, ↑/↓ choose, Tab/Enter insert, Esc closes.</summary>
    private void OnInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is AssistantViewModel { IsSkillListOpen: true } suggesting && HandleSkillKey(suggesting, e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != System.Windows.Input.Key.Enter
            || System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) return;
        e.Handled = true;
        if (DataContext is AssistantViewModel assistant && assistant.SendCommand.CanExecute(null))
            _ = assistant.SendCommand.ExecuteAsync(null);
    }

    private bool HandleSkillKey(AssistantViewModel assistant, System.Windows.Input.Key key)
    {
        switch (key)
        {
            case System.Windows.Input.Key.Down:
                assistant.MoveSkillSuggestion(+1);
                SkillList.ScrollIntoView(assistant.SelectedSkillSuggestion);
                return true;
            case System.Windows.Input.Key.Up:
                assistant.MoveSkillSuggestion(-1);
                SkillList.ScrollIntoView(assistant.SelectedSkillSuggestion);
                return true;
            case System.Windows.Input.Key.Tab:
            case System.Windows.Input.Key.Enter:
                if (assistant.SkillSuggestions.Count == 0) return false; // only the "no skills" hint: Enter sends
                AcceptSkill(assistant, null);
                return true;
            case System.Windows.Input.Key.Escape:
                assistant.IsSkillListOpen = false;
                return true;
            default:
                return false;
        }
    }

    private void OnSkillListClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not AssistantViewModel assistant) return;
        if ((e.OriginalSource as FrameworkElement)?.DataContext is Skill skill) AcceptSkill(assistant, skill);
    }

    private void AcceptSkill(AssistantViewModel assistant, Skill? skill)
    {
        assistant.AcceptSkillSuggestion(skill);
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    /// <summary>The suggestions close when the input loses focus (a click on them is handled first).</summary>
    private void OnInputLostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is AssistantViewModel assistant) assistant.IsSkillListOpen = false;
    }

    /// <summary>The Skills button opens its menu on a left click.</summary>
    private void OnSkillsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.DataContext = DataContext;
        menu.IsOpen = true;
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
