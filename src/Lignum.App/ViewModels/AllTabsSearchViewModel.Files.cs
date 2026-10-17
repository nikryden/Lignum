using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.Core;
using Lignum.Core.Searching;

namespace Lignum.App.ViewModels;

// Find in files (ticket 02): search the files of a folder that are not open, one at a time in the background,
// into the same shared results list. A click on a match opens the file in a tab at that line.
public sealed partial class AllTabsSearchViewModel
{
    private CancellationTokenSource? _folderCancel;

    /// <summary>The folder to search.</summary>
    [ObservableProperty]
    private string _folder = "";

    [ObservableProperty]
    private bool _includeSubfolders = true;

    /// <summary>Which files, e.g. <c>*.log;*.txt</c>; empty searches every file.</summary>
    [ObservableProperty]
    private string _filePatterns = "*.log;*.txt";

    /// <summary>The shared list shows a folder search (always shown, whatever <see cref="ShowResultsPerTab"/> says).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResultsListVisible))]
    private bool _isShowingFolderResults;

    /// <summary>Asks before a big folder is searched; false cancels. Null never asks.</summary>
    public Func<string, bool>? ConfirmLargeSearch { get; set; }

    /// <summary>Above this many files, ask first.</summary>
    internal int AskAboveFileCount { get; set; } = 1_000;

    /// <summary>Above this many bytes in total, ask first.</summary>
    internal long AskAboveTotalBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>Raised when the Find window should show its Find in files page.</summary>
    public event EventHandler? FindInFilesRequested;

    [RelayCommand]
    private void OpenFindInFiles() => FindInFilesRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Searches the folder's files for <see cref="FindText"/>; completes when done or cancelled.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchFolder()
    {
        Cancel();
        _files.Clear();
        Rows = [];
        Status = "";
        if (string.IsNullOrEmpty(FindText)) return;

        Query query;
        try
        {
            query = Query.Find(FindText, MatchCase, IsRegex, UseNonBacktracking);
        }
        catch (ArgumentException ex)
        {
            FindTextError = ex.Message;
            return;
        }

        if (!Directory.Exists(Folder))
        {
            FindTextError = $"Folder not found: {Folder}";
            return;
        }

        FindTextError = null;
        var generation = ++_generation;
        var cancel = new CancellationTokenSource();
        _folderCancel = cancel;
        IsShowingFolderResults = true;
        IsPaneOpen = true;
        IsSearching = true;
        Status = "Finding files…";
        try
        {
            var folder = Folder;
            var (files, totalBytes) = await Task.Run(
                () => FileFinder.Find(folder, IncludeSubfolders, FilePatterns, cancel.Token), cancel.Token);
            if (generation != _generation) return;
            if ((files.Count > AskAboveFileCount || totalBytes > AskAboveTotalBytes)
                && ConfirmLargeSearch?.Invoke(
                    $"Search {files.Count:N0} files ({totalBytes / (1024.0 * 1024 * 1024):N1} GB)? This can take a while.") == false)
            {
                IsSearching = false;
                Status = "Search cancelled";
                return;
            }

            var highlight = new Query(query.Rules, SearchViewModel.HighlightRegexTimeout);
            long matches = 0;
            int withMatches = 0, binary = 0, unreadable = 0;
            var sinceRebuild = Stopwatch.StartNew();
            for (var i = 0; i < files.Count; i++)
            {
                Status = $"Searching files… {i:N0} of {files.Count:N0} ({Matches(matches)})";
                var path = files[i];
                var (row, count, outcome) = await Task.Run(
                    () => SearchFileAsync(path, query, highlight, cancel.Token), cancel.Token);
                if (generation != _generation) return;
                switch (outcome)
                {
                    case FileOutcome.Binary: binary++; break;
                    case FileOutcome.Unreadable: unreadable++; break;
                }

                matches += count;
                if (row is null) continue;
                withMatches++;
                _files.Add(row);
                if (sinceRebuild.ElapsedMilliseconds < 300) continue; // don't rebuild the list for every file
                RebuildRows();
                sinceRebuild.Restart();
            }

            RebuildRows();
            IsSearching = false;
            Status = $"{Matches(matches)} in {withMatches:N0} of {files.Count:N0} files"
                     + (binary > 0 ? $"; {binary:N0} binary skipped" : "")
                     + (unreadable > 0 ? $"; {unreadable:N0} could not be read" : "");
        }
        catch (OperationCanceledException)
        {
            // Cancel() set the status.
        }
        finally
        {
            if (ReferenceEquals(_folderCancel, cancel)) _folderCancel = null;
            cancel.Dispose();
        }
    }

    private enum FileOutcome
    {
        Searched,
        Binary,
        Unreadable,
    }

    private static string Matches(long count) => count == 1 ? "1 match" : $"{count:N0} matches";

    /// <summary>Searches one file that is not open: indexed and read by position, never loaded whole.</summary>
    private static async Task<(AllTabsFileRow? Row, long Count, FileOutcome Outcome)> SearchFileAsync(
        string path, Query query, Query highlight, CancellationToken token)
    {
        try
        {
            if (FileFinder.LooksBinary(path)) return (null, 0, FileOutcome.Binary);
            using var document = LogDocument.Open(path);
            var result = document.StartQuery(query);
            var indexing = document.StartIndexing();
            _ = indexing.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // closing cancels it
            using (token.Register(result.Cancel))
            {
                var row = await ReadRows(null, path, document, result, highlight).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return (row, result.Count, FileOutcome.Searched);
            }
        }
        catch (Exception ex) when (LogDocument.IsOpenFailure(ex))
        {
            return (null, 0, FileOutcome.Unreadable);
        }
    }
}
