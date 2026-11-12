using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

/// <summary>Settings > Appearance (ticket 30): theme and tab width apply at once; the font on Apply.</summary>
public partial class AppearancePage : UserControl
{
    private readonly AppearanceViewModel _appearance;

    public AppearancePage(AppearanceViewModel appearance)
    {
        _appearance = appearance;
        DataContext = appearance;
        InitializeComponent();
        FamilyBox.ItemsSource = MonospacedFamilies();
        ShowCurrentFont();
    }

    private void ShowCurrentFont()
    {
        FamilyBox.Text = _appearance.FontFamily;
        SizeBox.Text = _appearance.FontSize.ToString(CultureInfo.CurrentCulture);
    }

    private void OnApplyFontClick(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(SizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var size))
        {
            FontStatus.Text = "Type a number for the size.";
            SizeBox.Focus();
            SizeBox.SelectAll();
            return;
        }

        _appearance.FontFamily = FamilyBox.Text;
        _appearance.FontSize = size;
        ShowCurrentFont(); // shows the size after clamping
        FontStatus.Text = "Applied.";
    }

    private void OnRevertFontClick(object sender, RoutedEventArgs e)
    {
        ShowCurrentFont();
        FontStatus.Text = "";
    }

    /// <summary>Installed fonts whose narrow and wide glyphs have the same advance width.</summary>
    private static List<string> MonospacedFamilies()
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out var glyphs)) continue;
            if (!glyphs.CharacterToGlyphMap.TryGetValue('i', out var narrow)
                || !glyphs.CharacterToGlyphMap.TryGetValue('W', out var wide)) continue;
            if (Math.Abs(glyphs.AdvanceWidths[narrow] - glyphs.AdvanceWidths[wide]) > 0.001) continue;
            names.Add(family.Source);
        }

        return [.. names];
    }
}
