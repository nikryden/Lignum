using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Find in files (ticket 02): search a folder's files that are not open, into the shared list.</summary>
public sealed class FindInFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public FindInFilesTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "logs", "old"));
        File.WriteAllLines(Path.Combine(_dir, "logs", "app.log"), ["start", "ERROR one", "ok"]);
        File.WriteAllLines(Path.Combine(_dir, "logs", "notes.txt"), ["error in notes"]);
        File.WriteAllLines(Path.Combine(_dir, "logs", "other.dat"), ["error in dat"]);
        File.WriteAllLines(Path.Combine(_dir, "logs", "old", "old.log"), ["x", "y", "error old"]);
        File.WriteAllBytes(Path.Combine(_dir, "logs", "image.log"), [0x89, 0x50, 0x00, 0x00, .. "error"u8.ToArray()]);
    }

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private MainViewModel Start()
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        main.AllTabsSearch.Folder = Path.Combine(_dir, "logs");
        main.AllTabsSearch.FindText = "error";
        return main;
    }

    private static string[] Describe(AllTabsSearchViewModel search) =>
    [
        .. search.Rows.Select(r => r switch
        {
            AllTabsFileRow file => file.Header,
            AllTabsMatchRow match => $"  {match.DisplayLine}: {match.Text}",
            _ => "?",
        }),
    ];

    [Fact]
    public async Task Matching_files_in_the_folder_and_subfolders_are_listed_and_binaries_skipped()
    {
        var search = Start().AllTabsSearch;

        await search.SearchFolderCommand.ExecuteAsync(null);

        Assert.Equal(
            ["app.log: 1 match", "  2: ERROR one", "notes.txt: 1 match", "  1: error in notes", "old.log: 1 match", "  3: error old"],
            Describe(search));
        Assert.Equal("3 matches in 3 of 4 files; 1 binary skipped", search.Status);
        Assert.True(search.IsResultsListVisible); // even though Per tab is on
    }

    [Fact]
    public async Task Subfolders_and_file_types_can_be_narrowed()
    {
        var search = Start().AllTabsSearch;
        search.IncludeSubfolders = false;
        search.FilePatterns = "*.log";

        await search.SearchFolderCommand.ExecuteAsync(null);

        Assert.Equal(["app.log: 1 match", "  2: ERROR one"], Describe(search));
    }

    [Fact]
    public async Task A_big_folder_asks_first_and_no_cancels()
    {
        var search = Start().AllTabsSearch;
        search.AskAboveFileCount = 2;
        string? asked = null;
        search.ConfirmLargeSearch = message =>
        {
            asked = message;
            return false;
        };

        await search.SearchFolderCommand.ExecuteAsync(null);

        Assert.StartsWith("Search 4 files", asked);
        Assert.Empty(search.Rows);
        Assert.Equal("Search cancelled", search.Status);
    }

    [Fact]
    public async Task Clicking_a_match_opens_the_file_at_that_line()
    {
        var main = Start();
        await main.AllTabsSearch.SearchFolderCommand.ExecuteAsync(null);
        var old = main.AllTabsSearch.Rows.OfType<AllTabsMatchRow>().Single(r => r.FileName == "old.log");

        await main.AllTabsSearch.GoToCommand.ExecuteAsync(old);

        Assert.Equal("old.log", main.SelectedTab!.FileName);
        Assert.Equal(2, main.SelectedTab.Navigation.Caret.Line);
    }

    [Fact]
    public async Task A_missing_folder_is_an_error()
    {
        var search = Start().AllTabsSearch;
        search.Folder = Path.Combine(_dir, "nope");

        await search.SearchFolderCommand.ExecuteAsync(null);

        Assert.StartsWith("Folder not found", search.FindTextError);
    }
}
