using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Lignum.App.Assistant;

namespace Lignum.App.Views;

/// <summary>Collapsed for true, visible for false.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The assistant side panel (ticket 05).</summary>
public partial class AssistantPanel : UserControl
{
    private bool _stickToBottom = true;

    public AssistantPanel()
    {
        Resources.Add("InverseBoolToVisibility", new InverseBoolToVisibilityConverter());
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) FocusInput();
        };
    }

    public void FocusInput() =>
        Dispatcher.BeginInvoke(() => InputBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);

    private void OnSaveKeyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AssistantViewModel assistant) return;
        assistant.SetApiKey(KeyBox.Password);
        KeyBox.Clear();
    }

    /// <summary>Enter sends; Shift+Enter adds a line.</summary>
    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        if (DataContext is AssistantViewModel assistant && assistant.SendCommand.CanExecute(null))
            _ = assistant.SendCommand.ExecuteAsync(null);
    }

    /// <summary>Keeps the newest text in view while the answer streams, unless the user scrolled up.</summary>
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0)
            _stickToBottom = Scroller.VerticalOffset >= Scroller.ScrollableHeight - 2;
        else if (_stickToBottom)
            Scroller.ScrollToEnd();
    }
}
