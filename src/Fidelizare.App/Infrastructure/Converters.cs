using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Fidelizare.App.Infrastructure;

/// <summary>Adevărat înseamnă vizibil.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Adevărat înseamnă ascuns.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

/// <summary>Se afișează doar dacă există o valoare.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Se afișează doar dacă lipsește valoarea. Folosit pentru mesajele „nu ai selectat nimic”.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Se afișează doar dacă textul nu este gol. Folosit pentru mesajele de eroare.</summary>
public sealed class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>Culoarea unui nivel, stocată ca text de forma #RRGGBB.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Fallback = new(Color.FromRgb(0x6B, 0x72, 0x80));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Fallback;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch (Exception)
        {
            // O culoare invalidă în baza de date nu trebuie să oprească afișarea.
            return Fallback;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Sume în lei, cu două zecimale și separatorul de mii.</summary>
public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Empty;

        var amount = System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        return amount.ToString("N2", culture) + " lei";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Puncte cu semn explicit, ca la extrasul de cont: +120 sau -50.</summary>
public sealed class SignedPointsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Empty;

        var points = System.Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return points > 0 ? $"+{points}" : points.ToString(culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Verde pentru puncte primite, roșu pentru puncte pierdute.</summary>
public sealed class PointsBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Positive = Freeze(Color.FromRgb(0x15, 0x80, 0x3D));
    private static readonly SolidColorBrush Negative = Freeze(Color.FromRgb(0xC0, 0x27, 0x2D));
    private static readonly SolidColorBrush Neutral = Freeze(Color.FromRgb(0x69, 0x70, 0x7D));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return Neutral;

        var points = System.Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return points > 0 ? Positive : points < 0 ? Negative : Neutral;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
