using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;

namespace Lignum.App.ViewModels;

/// <summary>The theme actually shown.</summary>
public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// Theme, font, zoom and tab width. Every change applies at once (bindings) and is saved to
/// <c>settings.json</c>. With <see cref="ThemeChoice.System"/> the theme follows Windows live.
/// <see cref="ThemeManager"/> turns <see cref="EffectiveTheme"/> into resource brushes.
/// </summary>
public sealed partial class AppearanceViewModel : ObservableObject
{
    public const int MinZoom = 50;
    public const int MaxZoom = 400;
    public const int ZoomStep = 10;
    public const int MinTabWidth = 1;
    public const int MaxTabWidth = 16;
    public const double MinFontSize = 6;
    public const double MaxFontSize = 72;

    private readonly SettingsFile<AppSettings> _file;
    private readonly ISystemTheme _windows;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    public AppearanceViewModel(SettingsFile<AppSettings> file, ISystemTheme windows)
    {
        _file = file;
        _windows = windows;
        var s = file.Value;
        s.FontFamily = string.IsNullOrWhiteSpace(s.FontFamily) ? AppSettings.DefaultFontFamily : s.FontFamily;
        s.FontSize = s.FontSize is >= MinFontSize and <= MaxFontSize ? s.FontSize : AppSettings.DefaultFontSize;
        s.Zoom = Math.Clamp(s.Zoom, MinZoom, MaxZoom);
        s.TabWidth = s.TabWidth is >= MinTabWidth and <= MaxTabWidth ? s.TabWidth : AppSettings.DefaultTabWidth;
        if (!Enum.IsDefined(s.Theme)) s.Theme = ThemeChoice.System;

        windows.Changed += OnWindowsThemeChanged;
    }

    private AppSettings S => _file.Value;

    public ThemeChoice ThemeChoice
    {
        get => S.Theme;
        set
        {
            if (S.Theme == value) return;
            S.Theme = value;
            Changed(nameof(ThemeChoice), nameof(EffectiveTheme));
        }
    }

    public AppTheme EffectiveTheme => S.Theme switch
    {
        ThemeChoice.Light => AppTheme.Light,
        ThemeChoice.Dark => AppTheme.Dark,
        _ => _windows.IsDark ? AppTheme.Dark : AppTheme.Light,
    };

    public string FontFamily
    {
        get => S.FontFamily;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? AppSettings.DefaultFontFamily : value.Trim();
            if (S.FontFamily == value) return;
            S.FontFamily = value;
            Changed(nameof(FontFamily));
        }
    }

    /// <summary>Base size in points; the viewer uses <see cref="EffectiveFontSize"/>.</summary>
    public double FontSize
    {
        get => S.FontSize;
        set
        {
            value = Math.Clamp(value, MinFontSize, MaxFontSize);
            if (S.FontSize == value) return;
            S.FontSize = value;
            Changed(nameof(FontSize), nameof(EffectiveFontSize));
        }
    }

    /// <summary>Zoom in percent.</summary>
    public int Zoom
    {
        get => S.Zoom;
        set
        {
            value = Math.Clamp(value, MinZoom, MaxZoom);
            if (S.Zoom == value) return;
            S.Zoom = value;
            Changed(nameof(Zoom), nameof(EffectiveFontSize));
            ZoomInCommand.NotifyCanExecuteChanged();
            ZoomOutCommand.NotifyCanExecuteChanged();
        }
    }

    public int TabWidth
    {
        get => S.TabWidth;
        set
        {
            value = Math.Clamp(value, MinTabWidth, MaxTabWidth);
            if (S.TabWidth == value) return;
            S.TabWidth = value;
            Changed(nameof(TabWidth));
        }
    }

    /// <summary>The size the viewer draws with: <see cref="FontSize"/> scaled by <see cref="Zoom"/>.</summary>
    public double EffectiveFontSize => S.FontSize * S.Zoom / 100.0;

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn() => Zoom += ZoomStep;

    private bool CanZoomIn() => Zoom < MaxZoom;

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut() => Zoom -= ZoomStep;

    private bool CanZoomOut() => Zoom > MinZoom;

    [RelayCommand]
    private void ResetZoom() => Zoom = AppSettings.DefaultZoom;

    [RelayCommand]
    private void SetTheme(ThemeChoice choice) => ThemeChoice = choice;

    [RelayCommand]
    private void SetTabWidth(int width) => TabWidth = width;

    private void Changed(params string[] properties)
    {
        foreach (var p in properties) OnPropertyChanged(p);
        _file.Save();
    }

    private void OnWindowsThemeChanged(object? sender, EventArgs e)
    {
        if (S.Theme != ThemeChoice.System) return;
        if (_uiContext is null) OnPropertyChanged(nameof(EffectiveTheme));
        else _uiContext.Post(_ => OnPropertyChanged(nameof(EffectiveTheme)), null);
    }
}
