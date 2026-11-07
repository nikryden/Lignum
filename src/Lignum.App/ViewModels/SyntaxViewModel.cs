using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Lignum.App.Viewer;
using Lignum.Core.Syntax;

namespace Lignum.App.ViewModels;

/// <summary>
/// Syntax coloring by file type (ticket 25), app-wide: the schemes, the one being edited, and the compiled
/// <see cref="Highlighters"/> for the current <see cref="Theme"/> that every viewer draws with. Every change
/// rebuilds them and saves <c>syntax-schemes.json</c>. <c>.txt</c>, <c>.log</c> and <c>.lm</c> never use a scheme.
/// </summary>
public sealed partial class SyntaxViewModel : ObservableObject
{
    private readonly SettingsFile<SyntaxSchemesFile> _file;
    private AppTheme _theme;
    private SyntaxHighlighterSet _highlighters = SyntaxHighlighterSet.None;
    private SyntaxSchemeViewModel? _selected;

    public SyntaxViewModel(SettingsFile<SyntaxSchemesFile> file)
    {
        _file = file;
        var firstRun = file.Value.Schemes is null;
        foreach (var scheme in file.Value.Schemes ?? BuiltInSyntaxSchemes.Create()) Add(scheme);
        _selected = Schemes.FirstOrDefault();
        Schemes.CollectionChanged += (_, _) => Changed();
        Rebuild();
        if (firstRun) Save();
    }

    public ObservableCollection<SyntaxSchemeViewModel> Schemes { get; } = [];

    /// <summary>The scheme shown in the editor.</summary>
    public SyntaxSchemeViewModel? SelectedScheme
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            DeleteSchemeCommand.NotifyCanExecuteChanged();
            DuplicateSchemeCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>The theme whose colors are used. Set by the app from <see cref="AppearanceViewModel.EffectiveTheme"/>.</summary>
    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme == value) return;
            _theme = value;
            OnPropertyChanged();
            Rebuild();
        }
    }

    /// <summary>All schemes compiled for <see cref="Theme"/>. Replaced (never mutated) on every change.</summary>
    public SyntaxHighlighterSet Highlighters
    {
        get => _highlighters;
        private set => SetProperty(ref _highlighters, value);
    }

    /// <summary>A copy of every scheme as it is now, for export and tests.</summary>
    public List<SyntaxScheme> Snapshot() => Schemes.Select(s => s.Data.Clone()).ToList();

    [RelayCommand]
    private void AddScheme()
    {
        var scheme = Add(new SyntaxScheme { Name = FreeName("New scheme") });
        SelectedScheme = scheme;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateScheme()
    {
        if (_selected is null) return;
        var copy = _selected.Data.Clone();
        copy.Name = FreeName(copy.Name + " copy");
        copy.Extensions = []; // two schemes for one type: only the first would ever be used
        SelectedScheme = Insert(Schemes.IndexOf(_selected) + 1, copy);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteScheme()
    {
        if (_selected is null) return;
        var index = Schemes.IndexOf(_selected);
        Schemes.RemoveAt(index);
        SelectedScheme = Schemes.Count == 0 ? null : Schemes[Math.Min(index, Schemes.Count - 1)];
    }

    private bool HasSelection() => _selected is not null;

    /// <summary>Goes back to the built-in schemes: every added scheme and every change is lost.</summary>
    public void ResetToFactoryDefaults()
    {
        var selectedName = _selected?.Name;
        ReplaceAll(BuiltInSyntaxSchemes.Create());
        SelectedScheme = Schemes.FirstOrDefault(s => s.Name == selectedName) ?? Schemes.FirstOrDefault();
    }

    /// <summary>Writes <paramref name="schemes"/> (null = all) to <paramref name="path"/> as a schemes file.</summary>
    public void Export(string path, IEnumerable<SyntaxSchemeViewModel>? schemes = null)
    {
        var file = new SyntaxSchemesFile { Schemes = (schemes ?? Schemes).Select(s => s.Data.Clone()).ToList() };
        File.WriteAllText(path, JsonSerializer.Serialize(file, SettingsStore.JsonOptions));
    }

    /// <summary>The schemes in an exported file (or a single exported scheme). Throws on a file that is not one.</summary>
    public static List<SyntaxScheme> ReadSchemes(string path)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        var schemes = document.RootElement.ValueKind == JsonValueKind.Object
                      && !document.RootElement.TryGetProperty("schemes", out _)
                      && !document.RootElement.TryGetProperty("Schemes", out _)
            ? [JsonSerializer.Deserialize<SyntaxScheme>(json, SettingsStore.JsonOptions)!]
            : JsonSerializer.Deserialize<SyntaxSchemesFile>(json, SettingsStore.JsonOptions)?.Schemes ?? [];
        schemes = schemes.Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name)).ToList();
        if (schemes.Count == 0) throw new InvalidDataException("The file has no syntax schemes.");
        return schemes;
    }

    /// <summary>
    /// Adds <paramref name="schemes"/>. One whose name is already used replaces that scheme when
    /// <paramref name="replace"/> is true, else it is added under a free name ("C# (2)"). Returns the count added.
    /// </summary>
    public int Import(IReadOnlyList<SyntaxScheme> schemes, bool replace)
    {
        SyntaxSchemeViewModel? last = null;
        foreach (var scheme in schemes)
        {
            var data = Normalize(scheme.Clone());
            var existing = Schemes.FirstOrDefault(s => string.Equals(s.Name, data.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && replace)
            {
                var index = Schemes.IndexOf(existing);
                Schemes.RemoveAt(index);
                last = Insert(index, data);
            }
            else
            {
                if (existing is not null) data.Name = FreeName(data.Name);
                last = Add(data);
            }
        }

        if (last is not null) SelectedScheme = last;
        return schemes.Count;
    }

    /// <summary>The names in <paramref name="schemes"/> that are already used.</summary>
    public IReadOnlyList<string> Conflicts(IEnumerable<SyntaxScheme> schemes) =>
        schemes.Where(s => Schemes.Any(e => string.Equals(e.Name, s.Name, StringComparison.OrdinalIgnoreCase)))
               .Select(s => s.Name).ToList();

    private void ReplaceAll(IEnumerable<SyntaxScheme> schemes)
    {
        _suspended = true;
        Schemes.Clear();
        foreach (var scheme in schemes) Add(scheme);
        _suspended = false;
        Changed();
    }

    private bool _suspended;

    private SyntaxSchemeViewModel Add(SyntaxScheme data) => Insert(Schemes.Count, data);

    private SyntaxSchemeViewModel Insert(int index, SyntaxScheme data)
    {
        var scheme = new SyntaxSchemeViewModel(Normalize(data), Changed);
        Schemes.Insert(index, scheme);
        return scheme;
    }

    private static SyntaxScheme Normalize(SyntaxScheme data)
    {
        data.Name = string.IsNullOrWhiteSpace(data.Name) ? "Scheme" : data.Name.Trim();
        data.Extensions = SyntaxFileTypes.ParseExtensions(string.Join(";", data.Extensions ?? []));
        data.Rules = (data.Rules ?? []).Where(r => r is not null).ToList();
        foreach (var rule in data.Rules)
        {
            rule.Name ??= "";
            rule.Pattern ??= "";
        }

        return data;
    }

    private string FreeName(string name)
    {
        var candidate = name;
        for (var n = 2; Schemes.Any(s => string.Equals(s.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} ({n})";
        return candidate;
    }

    private void Changed()
    {
        if (_suspended) return;
        Rebuild();
        Save();
    }

    private void Save()
    {
        _file.Value.Schemes = Schemes.Select(s => s.Data).ToList();
        _file.Save();
    }

    private void Rebuild() => Highlighters = new SyntaxHighlighterSet(Schemes.Select(s => s.Data).ToList(), _theme);
}

/// <summary>One scheme in the editor. Edits, rule additions, removals and moves are reported to the owner.</summary>
public sealed partial class SyntaxSchemeViewModel : ObservableObject
{
    private readonly Action _changed;

    internal SyntaxSchemeViewModel(SyntaxScheme data, Action changed)
    {
        Data = data;
        _changed = changed;
        foreach (var rule in data.Rules) Rules.Add(new SyntaxRuleViewModel(rule, changed));
        Rules.CollectionChanged += (_, _) =>
        {
            Data.Rules = Rules.Select(r => r.Data).ToList();
            _changed();
        };
    }

    internal SyntaxScheme Data { get; }

    public string Name
    {
        get => Data.Name;
        set
        {
            value = value?.Trim() ?? "";
            if (value.Length == 0 || value == Data.Name) return;
            Data.Name = value;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>The extensions as typed (<c>.cs; .csx</c>); log and text types are dropped.</summary>
    public string ExtensionsText
    {
        get => SyntaxFileTypes.FormatExtensions(Data.Extensions);
        set
        {
            var parsed = SyntaxFileTypes.ParseExtensions(value);
            if (!parsed.SequenceEqual(Data.Extensions))
            {
                Data.Extensions = parsed;
                _changed();
            }

            OnPropertyChanged(); // shows the cleaned-up list even when nothing changed
        }
    }

    public ObservableCollection<SyntaxRuleViewModel> Rules { get; } = [];

    [RelayCommand]
    private void AddRule()
    {
        var rule = new SyntaxRuleViewModel(new SyntaxRule { Name = "Token", Pattern = @"\bword\b" }, _changed);
        Rules.Add(rule);
    }

    [RelayCommand]
    private void RemoveRule(SyntaxRuleViewModel? rule)
    {
        if (rule is not null) Rules.Remove(rule);
    }

    [RelayCommand]
    private void MoveRuleUp(SyntaxRuleViewModel? rule) => Move(rule, -1);

    [RelayCommand]
    private void MoveRuleDown(SyntaxRuleViewModel? rule) => Move(rule, +1);

    private void Move(SyntaxRuleViewModel? rule, int delta)
    {
        if (rule is null) return;
        var index = Rules.IndexOf(rule);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Rules.Count) return;
        Rules.Move(index, target);
    }

    public override string ToString() => Name;
}

/// <summary>One token rule in the editor.</summary>
public sealed partial class SyntaxRuleViewModel : ObservableObject
{
    private readonly Action _changed;

    internal SyntaxRuleViewModel(SyntaxRule data, Action changed)
    {
        Data = data;
        _changed = changed;
    }

    internal SyntaxRule Data { get; }

    public string Name
    {
        get => Data.Name;
        set => Set(Data.Name, value ?? "", v => Data.Name = v, nameof(Name));
    }

    public string Pattern
    {
        get => Data.Pattern;
        set => Set(Data.Pattern, value ?? "", v => Data.Pattern = v, nameof(Pattern), nameof(Error));
    }

    public bool MatchCase
    {
        get => Data.MatchCase;
        set => Set(Data.MatchCase, value, v => Data.MatchCase = v, nameof(MatchCase));
    }

    public bool IsEnabled
    {
        get => Data.IsEnabled;
        set => Set(Data.IsEnabled, value, v => Data.IsEnabled = v, nameof(IsEnabled));
    }

    public string? LightColor
    {
        get => Data.LightColor;
        set => Set(Data.LightColor, Clean(value), v => Data.LightColor = v, nameof(LightColor));
    }

    public string? DarkColor
    {
        get => Data.DarkColor;
        set => Set(Data.DarkColor, Clean(value), v => Data.DarkColor = v, nameof(DarkColor));
    }

    public bool Bold
    {
        get => Data.Bold;
        set => Set(Data.Bold, value, v => Data.Bold = v, nameof(Bold));
    }

    public bool Italic
    {
        get => Data.Italic;
        set => Set(Data.Italic, value, v => Data.Italic = v, nameof(Italic));
    }

    /// <summary>Why the rule cannot be used (invalid regex), or null.</summary>
    public string? Error => SyntaxTokenizer.Validate(Data.Pattern);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Set<T>(T current, T value, Action<T> assign, params string[] properties)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        foreach (var p in properties) OnPropertyChanged(p);
        _changed();
    }
}
