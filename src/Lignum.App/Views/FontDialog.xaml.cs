using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Lignum.App.ViewModels;

namespace Lignum.App.Views;

/// <summary>Edits <see cref="AppearanceViewModel.FontFamily"/> and <see cref="AppearanceViewModel.FontSize"/>.</summary>
public partial class FontDialog : Window
{
    private readonly AppearanceViewModel _appearance;

    public FontDialog(AppearanceViewModel appearance)
    {
        _appearance = appearance;
        InitializeComponent();
        FamilyBox.ItemsSource = MonospacedFamilies();
        FamilyBox.Text = appearance.FontFamily;
        SizeBox.Text = appearance.FontSize.ToString(CultureInfo.CurrentCulture);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(SizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var size))
        {
            SizeBox.Focus();
            SizeBox.SelectAll();
            return;
        }

        _appearance.FontFamily = FamilyBox.Text;
        _appearance.FontSize = size;
        DialogResult = true;
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
