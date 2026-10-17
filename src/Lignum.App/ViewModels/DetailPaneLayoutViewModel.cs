using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;

namespace Lignum.App.ViewModels;

/// <summary>
/// Where the detail pane sits, the same for every tab: open or collapsed, docked right or at the bottom, and
/// its size in each position. Saved in settings.json (<c>detailPane</c>) on every change.
/// </summary>
public sealed partial class DetailPaneLayoutViewModel : ObservableObject
{
    /// <summary>Smallest width or height the pane can be dragged to.</summary>
    public const double MinPaneSize = 80;

    private readonly SettingsFile<AppSettings>? _settings;
    private readonly DetailPaneSettings _layout;

    /// <param name="settings">The app's one settings.json; null keeps the layout in memory only.</param>
    public DetailPaneLayoutViewModel(SettingsFile<AppSettings>? settings = null)
        : this(settings, s => s.DetailPane ??= new DetailPaneSettings())
    {
    }

    /// <param name="settings">The app's one settings.json; null keeps the layout in memory only.</param>
    /// <param name="layout">Which pane's settings in settings.json this layout uses (the AI panel has its own).</param>
    public DetailPaneLayoutViewModel(SettingsFile<AppSettings>? settings, Func<AppSettings, DetailPaneSettings> layout)
    {
        _settings = settings;
        _layout = settings is null ? layout(new AppSettings()) : layout(settings.Value);
        _layout.Width = ClampSize(_layout.Width, DetailPaneSettings.DefaultWidth);
        _layout.Height = ClampSize(_layout.Height, DetailPaneSettings.DefaultHeight);
        if (!Enum.IsDefined(_layout.Dock)) _layout.Dock = DetailPaneDock.Right;
    }

    public bool IsOpen
    {
        get => _layout.IsOpen;
        set => Set(_layout.IsOpen, value, v => _layout.IsOpen = v);
    }

    public DetailPaneDock Dock
    {
        get => _layout.Dock;
        set => Set(_layout.Dock, value, v => _layout.Dock = v);
    }

    /// <summary>Width when docked right.</summary>
    public double PaneWidth
    {
        get => _layout.Width;
        set => Set(_layout.Width, ClampSize(value, DetailPaneSettings.DefaultWidth), v => _layout.Width = v);
    }

    /// <summary>Height when docked at the bottom.</summary>
    public double PaneHeight
    {
        get => _layout.Height;
        set => Set(_layout.Height, ClampSize(value, DetailPaneSettings.DefaultHeight), v => _layout.Height = v);
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void DockRight() => Dock = DetailPaneDock.Right;

    [RelayCommand]
    private void DockBottom() => Dock = DetailPaneDock.Bottom;

    private void Set<T>(T current, T value, Action<T> store,
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        store(value);
        OnPropertyChanged(name);
        _settings?.Save();
    }

    private static double ClampSize(double value, double fallback) =>
        double.IsFinite(value) ? Math.Max(MinPaneSize, value) : fallback;
}
