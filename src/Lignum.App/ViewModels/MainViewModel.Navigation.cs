using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>Status bar and navigation (ticket 05): the caret, go to line, start/end of file, the file size.</summary>
public sealed partial class MainViewModel
{
    private NavigationViewModel? _navigation;

    /// <summary>The caret and the jumps that move it, over the lines indexed so far.</summary>
    public NavigationViewModel Navigation => _navigation ??= new NavigationViewModel(() => LineCount);

    /// <summary>Size of the open file in bytes when it was opened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileSizeText))]
    private long _fileSize;

    /// <summary>The file size for the status bar; empty with no file open.</summary>
    public string FileSizeText => FileName is null ? "" : ByteSize.Format(FileSize);

    private void AttachNavigation(LogDocument document)
    {
        FileSize = document.TotalBytes;
        Navigation.Reset();
    }
}
