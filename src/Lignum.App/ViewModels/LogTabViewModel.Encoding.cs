using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.Core;

namespace Lignum.App.ViewModels;

// The encoding in the status bar (ticket 06): what the document detected, and the user's override, which
// reopens the file with that encoding in the same tab (and is kept for later reloads of this tab).
public sealed partial class LogTabViewModel
{
    /// <summary>The encodings offered in the status bar menu.</summary>
    public static IReadOnlyList<LogEncoding> AvailableEncodings { get; } = Enum.GetValues<LogEncoding>();

    /// <summary>The document's encoding; null until indexing has examined the file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EncodingText))]
    private LogEncoding? _encoding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EncodingText))]
    private bool _hasByteOrderMark;

    /// <summary>True when the user chose the encoding instead of letting it be detected.</summary>
    [ObservableProperty]
    private bool _isEncodingOverridden;

    /// <summary>The encoding chosen by the user for this tab, or null to detect it.</summary>
    public LogEncoding? EncodingOverride { get; private set; }

    /// <summary>The encoding for the status bar, e.g. "UTF-8 BOM"; empty until it is known.</summary>
    public string EncodingText => Encoding?.DisplayName(HasByteOrderMark) ?? "";

    /// <summary>
    /// Reads the file again as <paramref name="encoding"/> (null = detect automatically), in this tab.
    /// Completes when the new indexing stops.
    /// </summary>
    [RelayCommand]
    private Task ChangeEncoding(LogEncoding? encoding)
    {
        var previous = EncodingOverride;
        EncodingOverride = encoding;
        var reopened = Reopen();
        if (ErrorMessage is not null) EncodingOverride = previous; // the file could not be reopened
        return reopened;
    }

    private void RefreshEncoding(LogDocument document)
    {
        Encoding = document.FileEncoding;
        HasByteOrderMark = document.HasByteOrderMark;
        IsEncodingOverridden = document.IsEncodingOverridden;
    }
}
