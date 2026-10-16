using Microsoft.Win32;

namespace Lignum.App.Settings;

/// <summary>The Windows light/dark app mode, injectable so tests can fake it.</summary>
public interface ISystemTheme
{
    bool IsDark { get; }

    /// <summary>The Windows setting may have changed. Can fire on any thread.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// Reads <c>AppsUseLightTheme</c> (read-only; Lignum never writes the registry) and watches
/// <see cref="SystemEvents.UserPreferenceChanged"/>.
/// </summary>
public sealed class WindowsSystemTheme : ISystemTheme, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public WindowsSystemTheme() => SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

    public bool IsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException
                                           or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public event EventHandler? Changed;

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
