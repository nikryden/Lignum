using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>Status bar and navigation (ticket 05): the caret, go to line, start/end of file, the file size.</summary>
public sealed partial class LogTabViewModel
{
    private NavigationViewModel? _navigation;

    /// <summary>This tab's caret and the jumps that move it, over the lines indexed so far.</summary>
    public NavigationViewModel Navigation => _navigation ??= new NavigationViewModel(() => LineCount);

    /// <summary>Size of the file in bytes when it was opened (or reloaded).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileSizeText))]
    private long _fileSize;

    /// <summary>The file size for the status bar.</summary>
    public string FileSizeText => ByteSize.Format(FileSize);

    /// <summary>A newly opened or reloaded document: its size, and the caret back at the start.</summary>
    private void AttachNavigation(LogDocument document)
    {
        FileSize = document.TotalBytes;
        Navigation.Reset();
    }
}
