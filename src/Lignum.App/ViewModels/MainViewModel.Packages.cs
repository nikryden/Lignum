using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Packaging;
using Lignum.App.Settings;

namespace Lignum.App.ViewModels;

// .lm packages (ticket 06): export the selected tab with its notes; open a package in a tab.
public sealed partial class MainViewModel
{
    private int _packageWork;
    private CancellationTokenSource _packageCancel = new();

    /// <summary>"Exporting app.lm… 42 %" while a package is written or extracted; then the outcome.</summary>
    [ObservableProperty]
    private string? _packageStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelPackageWorkCommand))]
    private bool _isPackageWorkRunning;

    /// <summary>Where opened packages are extracted (one sub-folder each); tests use a temp folder.</summary>
    public string PackageFolder { get; init; } = LogPackage.DefaultFolder;

    [RelayCommand(CanExecute = nameof(IsPackageWorkRunning))]
    private void CancelPackageWork() => _packageCancel.Cancel();

    /// <summary>
    /// Writes the selected tab's log, bookmarks and notes to <paramref name="destination"/> in the background.
    /// <paramref name="compress"/> false stores the log uncompressed (faster for huge logs). True when it was written.
    /// </summary>
    public async Task<bool> ExportPackageAsync(string destination, bool compress)
    {
        if (SelectedTab is not { } tab) return false;
        var name = Path.GetFileName(destination);
        var bookmarks = new BookmarksFile
        {
            Path = tab.FileName,
            Bookmarks =
            [
                .. tab.Bookmarks.Items.Select(b => new BookmarkEntry
                {
                    Line = b.Line, Note = b.Note, Color = b.Color == BookmarkColor.Blue ? null : b.Color,
                }),
            ],
        };
        try
        {
            await RunPackageWorkAsync($"Exporting {name}", async (progress, token) =>
            {
                await LogPackage.ExportAsync(tab.Path, bookmarks, tab.EncodingOverride, destination, compress,
                    progress, token);
                return true;
            });
            PackageStatus = $"Exported {name}";
            return true;
        }
        catch (OperationCanceledException)
        {
            PackageStatus = "Export cancelled";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Could not export {name}: {ex.Message}";
        }

        return false;
    }

    /// <summary>Extracts a package for a new tab; null (and <see cref="ErrorMessage"/> set unless cancelled) on failure.</summary>
    private async Task<OpenedPackage?> OpenPackageAsync(string fullPath, string displayPath)
    {
        try
        {
            return await RunPackageWorkAsync($"Opening {Path.GetFileName(fullPath)}",
                (progress, token) => LogPackage.OpenAsync(fullPath, PackageFolder, progress, token));
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException || Lignum.Core.LogDocument.IsOpenFailure(ex))
        {
            ErrorMessage = $"Could not open {displayPath}: {ex.Message}";
            return null;
        }
    }

    /// <summary>Runs package work with progress in <see cref="PackageStatus"/>; Cancel stops every running one.</summary>
    private async Task<T> RunPackageWorkAsync<T>(string label, Func<IProgress<double>, CancellationToken, Task<T>> work)
    {
        if (_packageWork++ == 0) _packageCancel = new CancellationTokenSource();
        IsPackageWorkRunning = true;
        PackageStatus = $"{label}…";
        var progress = new Progress<double>(done =>
        {
            if (IsPackageWorkRunning) PackageStatus = $"{label}… {done:P0}";
        });
        try
        {
            return await work(progress, _packageCancel.Token);
        }
        finally
        {
            if (--_packageWork == 0)
            {
                IsPackageWorkRunning = false;
                PackageStatus = null;
            }
        }
    }
}
