using Lignum.App.Settings;

namespace Lignum.App.ViewModels;

// Ticket 23: the bookmarks pane's width is saved and shared by every tab (each tab has its own pane).
public sealed partial class MainViewModel
{
    public double BookmarksPaneWidth
    {
        get => _settings.Value.BookmarksPaneWidth;
        set
        {
            value = double.IsFinite(value) ? Math.Clamp(value, 160, 4000) : AppSettings.DefaultBookmarksPaneWidth;
            if (value == _settings.Value.BookmarksPaneWidth) return;
            _settings.Value.BookmarksPaneWidth = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }
}
