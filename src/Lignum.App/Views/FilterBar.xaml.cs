using System.Windows.Controls;
using System.Windows.Threading;

namespace Lignum.App.Views;

/// <summary>The filter rule bar shown above the viewer (Search > Filter rules, Ctrl+Shift+L).</summary>
public partial class FilterBar : UserControl
{
    public FilterBar() => InitializeComponent();

    /// <summary>Puts the caret in the add box.</summary>
    public void FocusNewRuleBox() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => NewRuleBox.Focus());
}
