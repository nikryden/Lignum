using System.Windows;
using Lignum.App.ViewModels;

namespace Lignum.App.Themes;

/// <summary>
/// Applies an <see cref="AppTheme"/> to the application: WPF's Fluent theme (<see cref="Application.ThemeMode"/>)
/// restyles the standard controls (menus, scroll bars, buttons, text boxes), and our own
/// <c>Themes/Light.xaml</c> / <c>Themes/Dark.xaml</c> dictionary supplies the <see cref="ThemeKeys"/> brushes.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _current;

    /// <summary>Applies <paramref name="appearance"/>'s theme now and whenever it changes.</summary>
    public static void Follow(Application app, AppearanceViewModel appearance)
    {
        Apply(app, appearance.EffectiveTheme);
        appearance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppearanceViewModel.EffectiveTheme)) Apply(app, appearance.EffectiveTheme);
        };
    }

    public static void Apply(Application app, AppTheme theme)
    {
#pragma warning disable WPF0001 // ThemeMode is marked experimental.
        app.ThemeMode = theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Lignum;component/Themes/{theme}.xaml", UriKind.Absolute),
        };
        var merged = app.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dictionary); // after the Fluent dictionary, so our keys win
        _current = dictionary;
    }
}
