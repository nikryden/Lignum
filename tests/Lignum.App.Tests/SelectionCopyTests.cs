using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.Viewer;
using Lignum.App.ViewModels;
using Lignum.Core;
using Serilog.Core;

namespace Lignum.App.Tests;

public class SelectionCopyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeClipboard _clipboard = new();
    private readonly FakePrompt _prompt = new();

    public SelectionCopyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task<LogDocument> OpenIndexed(string content)
    {
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, content);
        var doc = LogDocument.Open(path);
        await doc.StartIndexing();
        return doc;
    }

    private SelectionCopier Copier(long threshold = 1_000_000) => new(_clipboard, _prompt, threshold);

    private static TextSelection Select(FilePosition from, FilePosition to)
    {
        var selection = new TextSelection();
        selection.MoveCaret(from, extend: false);
        selection.MoveCaret(to, extend: true);
        return selection;
    }

    [Fact]
    public async Task Copies_exactly_the_selected_text_with_its_line_breaks()
    {
        using var doc = await OpenIndexed("first line\r\nsecond\nthird line\n");
        var selection = Select(new FilePosition(2, 5), new FilePosition(0, 6)); // dragged upwards

        var result = await Copier().CopyAsync(doc, selection);

        Assert.Equal(CopyOutcome.Copied, result.Outcome);
        Assert.Equal(["line\r\nsecond\nthird"], _clipboard.Texts);
        Assert.Empty(_prompt.Asked);
    }

    [Fact]
    public async Task An_empty_selection_copies_nothing()
    {
        using var doc = await OpenIndexed("abc\n");

        var result = await Copier().CopyAsync(doc, new TextSelection());

        Assert.Equal(CopyOutcome.NothingSelected, result.Outcome);
        Assert.Empty(_clipboard.Texts);
    }

    [Fact]
    public async Task Select_all_then_copy_gives_the_whole_file_without_the_final_line_break()
    {
        using var doc = await OpenIndexed("a\r\nbb\nccc\n");
        var selection = new TextSelection();
        selection.SelectAll(doc.LineCount);

        await Copier().CopyAsync(doc, selection);

        Assert.Equal(["a\r\nbb\nccc"], _clipboard.Texts);
    }

    [Fact]
    public async Task Above_the_threshold_the_user_is_asked_first_and_can_cancel()
    {
        using var doc = await OpenIndexed(string.Concat(Enumerable.Repeat("0123456789\n", 20)));
        var selection = new TextSelection();
        selection.SelectAll(doc.LineCount);
        _prompt.Answer = LargeCopyChoice.Cancel;

        var result = await Copier(threshold: 100).CopyAsync(doc, selection);

        Assert.Equal(CopyOutcome.Cancelled, result.Outcome);
        Assert.Equal([220L], _prompt.Asked); // about the size: 20 lines of 11 bytes
        Assert.Empty(_clipboard.Texts);
    }

    [Fact]
    public async Task Above_the_threshold_the_user_can_copy_anyway()
    {
        using var doc = await OpenIndexed(string.Concat(Enumerable.Repeat("0123456789\n", 20)));
        var selection = new TextSelection();
        selection.SelectAll(doc.LineCount);
        _prompt.Answer = LargeCopyChoice.Copy;

        var result = await Copier(threshold: 100).CopyAsync(doc, selection);

        Assert.Equal(CopyOutcome.Copied, result.Outcome);
        Assert.Equal(219, Assert.Single(_clipboard.Texts).Length);
    }

    [Fact]
    public async Task Above_the_threshold_the_user_can_save_the_selection_to_a_file_instead()
    {
        using var doc = await OpenIndexed(string.Concat(Enumerable.Repeat("0123456789\r\n", 20)));
        var selection = Select(new FilePosition(1, 0), new FilePosition(19, 4));
        _prompt.Answer = LargeCopyChoice.SaveToFile;
        _prompt.SavePath = Path.Combine(_dir, "selection.txt");

        var result = await Copier(threshold: 100).CopyAsync(doc, selection);

        Assert.Equal(CopyOutcome.Saved, result.Outcome);
        Assert.Empty(_clipboard.Texts);
        var expected = string.Concat(Enumerable.Repeat("0123456789\r\n", 18)) + "0123";
        Assert.Equal(expected, File.ReadAllText(_prompt.SavePath));
    }

    [Fact]
    public async Task Choosing_no_file_to_save_to_cancels()
    {
        using var doc = await OpenIndexed(string.Concat(Enumerable.Repeat("0123456789\n", 20)));
        var selection = new TextSelection();
        selection.SelectAll(doc.LineCount);
        _prompt.Answer = LargeCopyChoice.SaveToFile;
        _prompt.SavePath = null;

        var result = await Copier(threshold: 100).CopyAsync(doc, selection);

        Assert.Equal(CopyOutcome.Cancelled, result.Outcome);
    }

    private MainViewModel CreateMain() => UiContext.None(() =>
        new MainViewModel(new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName))
        {
            Copier = new SelectionCopier(_clipboard, _prompt),
        });

    [Fact]
    public async Task A_tab_selects_all_and_copies_its_file()
    {
        using var vm = CreateMain();
        var path = Path.Combine(_dir, "vm.log");
        File.WriteAllText(path, "one\ntwo\n");
        await vm.OpenFileAsync(path);
        var tab = vm.SelectedTab!;

        tab.SelectAllCommand.Execute(null);
        await tab.CopyCommand.ExecuteAsync(null);

        Assert.Equal(["one\ntwo"], _clipboard.Texts);
        Assert.False(tab.IsCopying);
        Assert.Equal("Copied 7 characters", tab.CopyMessage);
    }

    [Fact]
    public async Task Select_all_copies_lines_longer_than_the_display_limit_in_full()
    {
        using var vm = CreateMain();
        var path = Path.Combine(_dir, "huge.log");
        var huge = new string('x', LineSource.DisplayLimit * 3) + "END";
        File.WriteAllText(path, $"short\n{huge}\n");
        await vm.OpenFileAsync(path);
        var tab = vm.SelectedTab!;

        tab.SelectAllCommand.Execute(null);
        await tab.CopyCommand.ExecuteAsync(null);

        Assert.Equal($"short\n{huge}", Assert.Single(_clipboard.Texts));
    }

    [Fact]
    public async Task Each_tab_has_its_own_selection()
    {
        using var vm = CreateMain();
        var first = Path.Combine(_dir, "first.log");
        var second = Path.Combine(_dir, "second.log");
        File.WriteAllText(first, "first\n");
        File.WriteAllText(second, "second\n");
        await vm.OpenFileAsync(first);
        vm.SelectedTab!.SelectAllCommand.Execute(null);

        await vm.OpenFileAsync(second);
        await vm.SelectedTab!.CopyCommand.ExecuteAsync(null);

        Assert.True(vm.SelectedTab.Selection.IsEmpty);
        Assert.Empty(_clipboard.Texts);
        Assert.False(vm.Tabs[0].Selection.IsEmpty);
    }

    [Fact]
    public async Task Extending_the_selection_moves_the_tab_caret()
    {
        using var vm = CreateMain();
        var path = Path.Combine(_dir, "caret.log");
        File.WriteAllText(path, "one\ntwo\nthree\n");
        await vm.OpenFileAsync(path);
        var tab = vm.SelectedTab!;

        tab.Selection.MoveCaret(new FilePosition(2, 3), extend: true);

        Assert.Equal(new FilePosition(2, 3), tab.Navigation.Caret);
        Assert.Equal("Ln 3, Col 4", tab.Navigation.CaretText);
        Assert.False(tab.Selection.IsEmpty);
    }

    [Fact]
    public async Task Moving_the_caret_without_extending_clears_the_selection()
    {
        using var vm = CreateMain();
        var path = Path.Combine(_dir, "caret.log");
        File.WriteAllText(path, "one\ntwo\nthree\n");
        await vm.OpenFileAsync(path);
        var tab = vm.SelectedTab!;
        tab.SelectAllCommand.Execute(null);

        tab.Navigation.GoToEndCommand.Execute(null); // Ctrl+End; plain arrows and Go to line work the same way

        Assert.True(tab.Selection.IsEmpty);
        Assert.Equal(new FilePosition(2, 0), tab.Selection.Caret);
    }

    [Fact]
    public async Task Select_all_leaves_the_caret_at_the_start_of_the_file()
    {
        using var vm = CreateMain();
        var path = Path.Combine(_dir, "caret.log");
        File.WriteAllText(path, "one\ntwo\nthree\n");
        await vm.OpenFileAsync(path);
        var tab = vm.SelectedTab!;
        tab.Navigation.GoToEndCommand.Execute(null);

        tab.SelectAllCommand.Execute(null);

        Assert.Equal(FilePosition.Start, tab.Navigation.Caret);
        Assert.Equal(new FilePosition(2, int.MaxValue), tab.Selection.End);
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public List<string> Texts { get; } = [];

        public void SetText(string text) => Texts.Add(text);
    }

    private sealed class FakePrompt : ICopyPrompt
    {
        public LargeCopyChoice Answer { get; set; }
        public string? SavePath { get; set; }
        public List<long> Asked { get; } = [];

        public LargeCopyChoice AskLargeCopy(long approximateBytes)
        {
            Asked.Add(approximateBytes);
            return Answer;
        }

        public string? AskSavePath() => SavePath;
    }
}
