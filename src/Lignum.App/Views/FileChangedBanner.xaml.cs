using System.Windows.Controls;

namespace Lignum.App.Views;

/// <summary>Shown above the viewer when the open file changes on disk; offers Reload.</summary>
public partial class FileChangedBanner : UserControl
{
    public FileChangedBanner() => InitializeComponent();
}
