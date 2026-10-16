using System.Globalization;

namespace Lignum.App.ViewModels;

/// <summary>Formats a byte count for people: "1,023 bytes", "1.5 KB", "2.00 GB" (binary units).</summary>
public static class ByteSize
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    public static string Format(long bytes, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < 1024) return string.Format(culture, bytes == 1 ? "{0:N0} byte" : "{0:N0} bytes", bytes);

        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // Two decimals for GB and up, where 0.1 is still a lot of bytes.
        var format = unit >= 2 ? "{0:N2} {1}" : "{0:N1} {1}";
        return string.Format(culture, format, value, Units[unit]);
    }
}
