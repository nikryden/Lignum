using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using Lignum.App.Viewer;
using Lignum.App.ViewModels;
using Microsoft.Win32;

namespace Lignum.App.Views;

/// <summary>Edits <see cref="SyntaxViewModel"/> schemes (ticket 25); every change applies at once.</summary>
public partial class SyntaxSchemesDialog : Window
{
    private const string JsonFilter = "Lignum syntax schemes (*.json)|*.json|All files (*.*)|*.*";

    private const string DefaultSample =
        "// A comment\n" +
        "public static int Count(string name) => name.Length + 42; /* note */\n" +
        "if (ready) { return \"done\"; }\n" +
        "# key: value\n" +
        "<Item Name=\"x\" Enabled='true' />";

    private readonly SyntaxViewModel _syntax;

    public SyntaxSchemesDialog(SyntaxViewModel syntax)
    {
        _syntax = syntax;
        DataContext = syntax;
        InitializeComponent();
        SampleInput.Text = DefaultSample;
        syntax.PropertyChanged += OnSyntaxChanged;
        Closed += (_, _) => syntax.PropertyChanged -= OnSyntaxChanged;
        // Commit a cell being edited when the dialog closes, so the last edit isn't lost.
        Closing += (_, _) => RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        RenderSample();
    }

    private void OnSyntaxChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SyntaxViewModel.Highlighters) or nameof(SyntaxViewModel.SelectedScheme)) RenderSample();
    }

    private void OnSampleChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RenderSample();

    /// <summary>Colors the sample lines with the selected scheme and the current theme, as the viewer would.</summary>
    private void RenderSample()
    {
        if (SampleOutput is null || SampleInput is null) return;
        SampleOutput.Inlines.Clear();
        var highlighter = _syntax.SelectedScheme is { } scheme ? new SyntaxHighlighter(scheme.Data, _syntax.Theme) : null;
        var lines = SampleInput.Text.Replace("\r\n", "\n").Split('\n');
        for (var l = 0; l < lines.Length; l++)
        {
            if (l > 0) SampleOutput.Inlines.Add(new LineBreak());
            var line = lines[l];
            var at = 0;
            foreach (var token in highlighter?.Tokenize(line) ?? [])
            {
                if (token.Start > at) SampleOutput.Inlines.Add(new Run(line[at..token.Start]));
                var style = highlighter!.StyleOf(token);
                var run = new Run(line.Substring(token.Start, token.Length)) { FontWeight = style.Weight, FontStyle = style.Style };
                if (style.Foreground is { } brush) run.Foreground = brush;
                SampleOutput.Inlines.Add(run);
                at = token.Start + token.Length;
            }

            if (at < line.Length) SampleOutput.Inlines.Add(new Run(line[at..]));
        }
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import syntax schemes", Filter = JsonFilter };
        if (dialog.ShowDialog(this) != true) return;

        List<Core.Syntax.SyntaxScheme> schemes;
        try
        {
            schemes = SyntaxViewModel.ReadSchemes(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.Show(this, $"Could not import {Path.GetFileName(dialog.FileName)}: {ex.Message}", "Import",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var replace = false;
        if (_syntax.Conflicts(schemes) is { Count: > 0 } conflicts)
        {
            var answer = MessageBox.Show(this,
                $"These schemes already exist: {string.Join(", ", conflicts)}.\n\nYes: replace them.  No: keep both.",
                "Import", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            replace = answer == MessageBoxResult.Yes;
        }

        CommitEdits();
        var count = _syntax.Import(schemes, replace);
        Status.Text = $"Imported {count:N0} scheme{(count == 1 ? "" : "s")}.";
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_syntax.SelectedScheme is { } scheme) Export([scheme], SafeFileName(scheme.Name));
    }

    private void OnExportAllClick(object sender, RoutedEventArgs e) => Export(null, "syntax-schemes");

    private void Export(IEnumerable<SyntaxSchemeViewModel>? schemes, string name)
    {
        CommitEdits();
        var dialog = new SaveFileDialog { Title = "Export syntax schemes", Filter = JsonFilter, FileName = name + ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _syntax.Export(dialog.FileName, schemes);
            Status.Text = $"Exported to {dialog.FileName}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not export: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnFactoryDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "This brings back the built-in schemes and deletes added schemes and every change. Continue?",
                "Factory defaults", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        RulesGrid.CancelEdit(System.Windows.Controls.DataGridEditingUnit.Row);
        _syntax.ResetToFactoryDefaults();
        Status.Text = "Built-in schemes restored.";
    }

    private void CommitEdits() => RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c));
}
