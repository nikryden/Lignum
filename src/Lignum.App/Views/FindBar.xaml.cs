using System.Windows.Controls;
using System.Windows.Threading;

namespace Lignum.App.Views;

/// <summary>The find box shown above the viewer by Ctrl+F.</summary>
public partial class FindBar : UserControl
{
    public FindBar() => InitializeComponent();

    /// <summary>Puts the caret in the find box with its text selected, ready to type over.</summary>
    public void FocusFindBox()
    {
        // The bar may only just have become visible; focus once layout has run.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            FindBox.Focus();
            FindBox.SelectAll();
        });
    }
}
