using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

public sealed class AppearanceViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeSystemTheme _windows = new() { IsDark = false };

    public void Dispose()
    {
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private AppearanceViewModel Create() => UiContext.None(() =>
        new AppearanceViewModel(new SettingsStore(_dir, Logger.None).Open<AppSettings>(AppSettings.FileName), _windows));

    [Fact]
    public void By_default_the_theme_follows_Windows()
    {
        _windows.IsDark = true;
        var vm = Create();

        Assert.Equal(ThemeChoice.System, vm.ThemeChoice);
        Assert.Equal(AppTheme.Dark, vm.EffectiveTheme);
    }

    [Fact]
    public void When_following_Windows_a_change_in_Windows_switches_the_theme()
    {
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => { lock (changed) changed.Add(e.PropertyName); };

        _windows.IsDark = true;
        _windows.RaiseChanged();

        Assert.Equal(AppTheme.Dark, vm.EffectiveTheme);
        // xUnit runs tests under its own SynchronizationContext, so the VM posts the notification: wait for it.
        Assert.True(SpinWait.SpinUntil(() =>
        {
            lock (changed) return changed.Contains(nameof(AppearanceViewModel.EffectiveTheme));
        }, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_chosen_theme_ignores_Windows_and_persists_across_restarts()
    {
        var vm = Create();
        vm.ThemeChoice = ThemeChoice.Dark;
        _windows.RaiseChanged();

        Assert.Equal(AppTheme.Dark, vm.EffectiveTheme);
        var restarted = Create();
        Assert.Equal(ThemeChoice.Dark, restarted.ThemeChoice);
        Assert.Equal(AppTheme.Dark, restarted.EffectiveTheme);
    }

    [Fact]
    public void Font_and_tab_width_changes_apply_immediately_and_persist()
    {
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.FontFamily = "Consolas";
        vm.FontSize = 16;
        vm.TabWidth = 8;

        Assert.Equal(16, vm.EffectiveFontSize);
        Assert.Contains(nameof(AppearanceViewModel.EffectiveFontSize), changed);
        var restarted = Create();
        Assert.Equal("Consolas", restarted.FontFamily);
        Assert.Equal(16, restarted.FontSize);
        Assert.Equal(8, restarted.TabWidth);
    }

    [Fact]
    public void Zooming_scales_the_font_and_is_remembered()
    {
        var vm = Create(); // 13 pt at 100 %

        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);

        Assert.Equal(120, vm.Zoom);
        Assert.Equal(13 * 1.2, vm.EffectiveFontSize, 6);
        Assert.Equal(120, Create().Zoom);

        vm.ZoomOutCommand.Execute(null);
        Assert.Equal(110, vm.Zoom);
        vm.ResetZoomCommand.Execute(null);
        Assert.Equal(100, vm.Zoom);
    }

    [Fact]
    public void Zoom_stays_within_its_limits()
    {
        var vm = Create();

        for (var i = 0; i < 100; i++) vm.ZoomOutCommand.Execute(null);
        Assert.Equal(AppearanceViewModel.MinZoom, vm.Zoom);

        for (var i = 0; i < 100; i++) vm.ZoomInCommand.Execute(null);
        Assert.Equal(AppearanceViewModel.MaxZoom, vm.Zoom);
    }

    [Fact]
    public void Out_of_range_values_in_the_file_are_corrected()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{ "fontFamily": "", "fontSize": -3, "zoom": 100000, "tabWidth": 0 }""");

        var vm = Create();

        Assert.Equal(AppSettings.DefaultFontFamily, vm.FontFamily);
        Assert.Equal(AppSettings.DefaultFontSize, vm.FontSize);
        Assert.Equal(AppearanceViewModel.MaxZoom, vm.Zoom);
        Assert.Equal(AppSettings.DefaultTabWidth, vm.TabWidth);
    }

    private sealed class FakeSystemTheme : ISystemTheme
    {
        public bool IsDark { get; set; }

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
