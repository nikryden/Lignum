using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

/// <summary>
/// Line coloring (ticket 14), app-wide. Holds named presets of ordered whole-line rules, the active preset,
/// and the compiled <see cref="Highlighter"/> for the current <see cref="Theme"/> that every viewer draws with.
/// Every change (rules, order, presets) rebuilds the highlighter and saves <c>highlight-presets.json</c>.
/// </summary>
public sealed partial class HighlightingViewModel : ObservableObject
{
    public const string DefaultPresetName = "Default";

    // Background colors handed out to rules added from a selection: (light, dark), both readable with the
    // theme's text color and distinct from the search match color.
    private static readonly (string Light, string Dark)[] Palette =
    [
        ("#C8F0C8", "#1E4620"),
        ("#CCE4FF", "#1C3A5E"),
        ("#F0D0F0", "#4A2050"),
        ("#FFE0C0", "#5A3618"),
        ("#D0F0F0", "#1A4848"),
        ("#E8E0FF", "#38305E"),
    ];

    private readonly SettingsFile<HighlightPresetsFile> _file;
    private HighlightPresetViewModel? _activePreset;
    private AppTheme _theme;
    private LineHighlighter _highlighter = LineHighlighter.None;
    private int _nextPaletteColor;

    public HighlightingViewModel(SettingsFile<HighlightPresetsFile> file)
    {
        _file = file;
        var data = file.Value;
        var firstRun = data.Presets is null || data.Presets.Count == 0;
        if (firstRun) data.Presets = [CreateDefaultPreset()];

        foreach (var preset in data.Presets!)
        {
            preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? DefaultPresetName : preset.Name.Trim();
            preset.Rules ??= [];
            Presets.Add(new HighlightPresetViewModel(preset, OnRulesChanged));
        }

        _activePreset = Presets.FirstOrDefault(p => p.Name == data.ActivePreset) ?? Presets[0];
        data.ActivePreset = _activePreset.Name;
        Rebuild();
        if (firstRun) file.Save();
    }

    public ObservableCollection<HighlightPresetViewModel> Presets { get; } = [];

    /// <summary>The preset whose rules color the lines. Setting it switches presets.</summary>
    public HighlightPresetViewModel? ActivePreset
    {
        get => _activePreset;
        set
        {
            if (value is null || ReferenceEquals(value, _activePreset) || !Presets.Contains(value)) return;
            _activePreset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Rules));
            DeletePresetCommand.NotifyCanExecuteChanged();
            Save();
        }
    }

    /// <summary>The active preset's rules, in priority order (the first match wins).</summary>
    public ObservableCollection<HighlightRuleViewModel> Rules => _activePreset!.Rules;

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

    /// <summary>The compiled active rules for <see cref="Theme"/>. Replaced (never mutated) on every change.</summary>
    public LineHighlighter Highlighter
    {
        get => _highlighter;
        private set => SetProperty(ref _highlighter, value);
    }

    /// <summary>
    /// Adds a rule at the top of the active preset (so it wins over the others) with a background color for
    /// both themes, and returns it. Entry point for "Add as highlight rule" on a selection.
    /// </summary>
    public HighlightRuleViewModel AddRule(string pattern, bool isRegex = false, bool matchCase = false)
    {
        var (light, dark) = Palette[_nextPaletteColor++ % Palette.Length];
        var rule = new HighlightRuleData
        {
            Pattern = pattern,
            IsRegex = isRegex,
            MatchCase = matchCase,
            Light = new HighlightColors { Background = light },
            Dark = new HighlightColors { Background = dark },
        };
        return _activePreset!.Insert(0, rule);
    }

    [RelayCommand]
    private void NewRule() => AddRule("text");

    [RelayCommand]
    private void RemoveRule(HighlightRuleViewModel? rule)
    {
        if (rule is not null) Rules.Remove(rule);
    }

    [RelayCommand]
    private void MoveRuleUp(HighlightRuleViewModel? rule) => Move(rule, -1);

    [RelayCommand]
    private void MoveRuleDown(HighlightRuleViewModel? rule) => Move(rule, +1);

    private void Move(HighlightRuleViewModel? rule, int delta)
    {
        if (rule is null) return;
        var index = Rules.IndexOf(rule);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Rules.Count) return;
        Rules.Move(index, target);
    }

    /// <summary>Saves the active preset's rules as a new preset named <paramref name="name"/> and switches to it.</summary>
    /// <returns>The new preset, or null when the name is empty or already used.</returns>
    public HighlightPresetViewModel? CreatePreset(string name)
    {
        name = name.Trim();
        if (!IsFreeName(name)) return null;
        var data = new HighlightPresetData
        {
            Name = name,
            Rules = _activePreset!.Rules.Select(r => r.Data.Clone()).ToList(),
        };
        var preset = new HighlightPresetViewModel(data, OnRulesChanged);
        Presets.Add(preset);
        ActivePreset = preset;
        return preset;
    }

    /// <summary>Renames the active preset. False when the name is empty or used by another preset.</summary>
    public bool RenamePreset(string name)
    {
        name = name.Trim();
        if (name == _activePreset!.Name) return true;
        if (!IsFreeName(name)) return false;
        _activePreset.Name = name;
        Save();
        return true;
    }

    /// <summary>Deletes the active preset and switches to a neighbour. The last preset cannot be deleted.</summary>
    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private void DeletePreset()
    {
        if (!CanDeletePreset()) return;
        var index = Presets.IndexOf(_activePreset!);
        var doomed = _activePreset!;
        ActivePreset = Presets[index + 1 < Presets.Count ? index + 1 : index - 1];
        Presets.Remove(doomed);
        DeletePresetCommand.NotifyCanExecuteChanged();
        Save();
    }

    private bool CanDeletePreset() => Presets.Count > 1;

    private bool IsFreeName(string name) =>
        name.Length > 0 && !Presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private void OnRulesChanged(HighlightPresetViewModel preset)
    {
        if (ReferenceEquals(preset, _activePreset)) Rebuild();
        _file.Value.Presets = Presets.Select(p => p.Data).ToList();
        _file.Save();
    }

    private void Save()
    {
        _file.Value.Presets = Presets.Select(p => p.Data).ToList();
        _file.Value.ActivePreset = _activePreset!.Name;
        _file.Save();
        Rebuild();
    }

    private void Rebuild() => Highlighter = LineHighlighter.Build(_activePreset!.Rules.Select(r => r.Data), _theme);

    /// <summary>ERROR (and FATAL), WARN and INFO, in the common long and short spellings, for both themes.</summary>
    public static HighlightPresetData CreateDefaultPreset() => new()
    {
        Name = DefaultPresetName,
        Rules =
        [
            new HighlightRuleData
            {
                Pattern = @"\b(ERROR|ERR|FATAL|FTL|CRITICAL|CRIT)\b",
                IsRegex = true,
                MatchCase = true,
                Light = new HighlightColors { Foreground = "#A80000", Background = "#FDE7E9" },
                Dark = new HighlightColors { Foreground = "#FF9A9A", Background = "#4B1D1D" },
            },
            new HighlightRuleData
            {
                Pattern = @"\b(WARN|WARNING|WRN)\b",
                IsRegex = true,
                MatchCase = true,
                Light = new HighlightColors { Foreground = "#8A4B00", Background = "#FFF1DC" },
                Dark = new HighlightColors { Foreground = "#FFC870", Background = "#3D2E12" },
            },
            new HighlightRuleData
            {
                Pattern = @"\b(INFO|INF)\b",
                IsRegex = true,
                MatchCase = true,
                Light = new HighlightColors { Foreground = "#0B5394" },
                Dark = new HighlightColors { Foreground = "#8CC4FF" },
            },
        ],
    };
}

/// <summary>A named rule set. Rule edits, additions, removals and moves are reported to the owner.</summary>
public sealed partial class HighlightPresetViewModel : ObservableObject
{
    private readonly Action<HighlightPresetViewModel> _changed;

    internal HighlightPresetViewModel(HighlightPresetData data, Action<HighlightPresetViewModel> changed)
    {
        Data = data;
        _changed = changed;
        foreach (var rule in data.Rules) Rules.Add(new HighlightRuleViewModel(rule, OnRuleChanged));
        Rules.CollectionChanged += (_, _) =>
        {
            Data.Rules = Rules.Select(r => r.Data).ToList();
            _changed(this);
        };
    }

    internal HighlightPresetData Data { get; }

    public string Name
    {
        get => Data.Name;
        internal set
        {
            if (Data.Name == value) return;
            Data.Name = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<HighlightRuleViewModel> Rules { get; } = [];

    internal HighlightRuleViewModel Insert(int index, HighlightRuleData data)
    {
        var rule = new HighlightRuleViewModel(data, OnRuleChanged);
        Rules.Insert(index, rule);
        return rule;
    }

    private void OnRuleChanged() => _changed(this);

    public override string ToString() => Name;
}

/// <summary>One editable rule. Colors are text (<c>#RRGGBB</c>); invalid or empty means no color.</summary>
public sealed partial class HighlightRuleViewModel : ObservableObject
{
    private readonly Action _changed;

    internal HighlightRuleViewModel(HighlightRuleData data, Action changed)
    {
        Data = data;
        _changed = changed;
        data.Light ??= new HighlightColors();
        data.Dark ??= new HighlightColors();
        data.Pattern ??= "";
    }

    internal HighlightRuleData Data { get; }

    public string Pattern
    {
        get => Data.Pattern;
        set => Set(Data.Pattern, value ?? "", v => Data.Pattern = v, nameof(Pattern), nameof(Error));
    }

    public bool IsRegex
    {
        get => Data.IsRegex;
        set => Set(Data.IsRegex, value, v => Data.IsRegex = v, nameof(IsRegex), nameof(Error));
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

    public string? LightForeground
    {
        get => Data.Light.Foreground;
        set => Set(Data.Light.Foreground, Clean(value), v => Data.Light.Foreground = v, nameof(LightForeground));
    }

    public string? LightBackground
    {
        get => Data.Light.Background;
        set => Set(Data.Light.Background, Clean(value), v => Data.Light.Background = v, nameof(LightBackground));
    }

    public string? DarkForeground
    {
        get => Data.Dark.Foreground;
        set => Set(Data.Dark.Foreground, Clean(value), v => Data.Dark.Foreground = v, nameof(DarkForeground));
    }

    public string? DarkBackground
    {
        get => Data.Dark.Background;
        set => Set(Data.Dark.Background, Clean(value), v => Data.Dark.Background = v, nameof(DarkBackground));
    }

    /// <summary>Why the rule cannot be used (invalid regex), or null.</summary>
    public string? Error
    {
        get
        {
            if (!Data.IsRegex || Data.Pattern.Length == 0) return null;
            try
            {
                _ = new System.Text.RegularExpressions.Regex(Data.Pattern,
                    System.Text.RegularExpressions.RegexOptions.None, LineHighlighter.RegexTimeout);
                return null;
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Set<T>(T current, T value, Action<T> assign, params string[] properties)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        foreach (var p in properties) OnPropertyChanged(p);
        _changed();
    }
}
