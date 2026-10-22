using System.ComponentModel;
using System.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Lignum.Core;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>File → Open in editor (ticket 13).</summary>
public sealed class ExternalEditorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public ExternalEditorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SettingsStore.Flush();
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private MainViewModel Start() => UiContext.None(() =>
        new MainViewModel(new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));

    private string WriteLog()
    {
        var path = Path.Combine(_dir, "a b.log");
        File.WriteAllLines(path, Enumerable.Range(0, 50).Select(i => $"line {i}"));
        return path;
    }

    [Theory]
    [InlineData("\"{file}\"", "\"C:\\x y.log\"")]
    [InlineData("-n{line} \"{file}\"", "-n42 \"C:\\x y.log\"")]
    [InlineData("-g \"{FILE}:{Line}\"", "-g \"C:\\x y.log:42\"")]
    [InlineData("", "\"C:\\x y.log\"")]
    public void The_argument_template_takes_the_file_and_line(string template, string expected) =>
        Assert.Equal(expected, ExternalEditor.ExpandArguments(template, @"C:\x y.log", 42));

    [Fact]
    public async Task Open_in_editor_starts_the_configured_editor_at_the_caret_line()
    {
        using var vm = Start();
        var started = new List<ProcessStartInfo>();
        vm.StartProcess = started.Add;
        vm.SetExternalEditor("notepad++.exe", "-n{line} \"{file}\"");
        var path = WriteLog();
        await vm.OpenFileAsync(path);
        vm.SelectedTab!.Navigation.Caret = new FilePosition(9, 0);

        vm.OpenInEditorCommand.Execute(null);
        vm.OpenInEditorCommand.Execute(19L);

        Assert.Equal(["notepad++.exe", "notepad++.exe"], started.Select(s => s.FileName));
        Assert.Equal([$"-n10 \"{path}\"", $"-n20 \"{path}\""], started.Select(s => s.Arguments));
    }

    [Fact]
    public void The_editor_choice_is_saved()
    {
        using (var vm = Start()) vm.SetExternalEditor(" code ", "-g \"{file}:{line}\"");

        using var again = Start();

        Assert.Equal("code", again.ExternalEditor.Command);
        Assert.Equal("-g \"{file}:{line}\"", again.ExternalEditor.Arguments);
    }

    [Fact]
    public async Task A_missing_editor_shows_an_error_instead_of_crashing()
    {
        using var vm = Start();
        vm.StartProcess = _ => throw new Win32Exception(2, "The system cannot find the file specified");
        await vm.OpenFileAsync(WriteLog());

        vm.OpenInEditorCommand.Execute(null);

        Assert.Contains("Could not start the editor", vm.ErrorMessage);
    }
}
