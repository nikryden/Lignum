using System.Windows.Controls;

namespace Lignum.App.Views;

/// <summary>One window's status bar (ticket 29); DataContext is the window's <see cref="ViewModels.TabGroupViewModel"/>.</summary>
public partial class TabStatusBar : UserControl
{
    public TabStatusBar() => InitializeComponent();
}
