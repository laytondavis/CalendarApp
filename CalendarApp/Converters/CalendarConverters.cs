using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI.Text;

namespace CalendarApp.Converters;

/// <summary>
/// Converts bool to FontWeight (Bold for true, Normal for false).
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? FontWeights.Bold : FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to Foreground brush (White for true, default for false).
/// </summary>
public class BoolToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            return new SolidColorBrush(Colors.White);
        }
        return Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
               ?? new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to Opacity (1.0 for true, 0.5 for false).
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? 1.0 : 0.4;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts null/empty string to Visibility.Collapsed.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null)
            return Visibility.Collapsed;

        if (value is string s && string.IsNullOrEmpty(s))
            return Visibility.Collapsed;

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to Visibility.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a bool value.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && !b;
    }
}

/// <summary>
/// Converts a hex color string to a SolidColorBrush.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    var r = System.Convert.ToByte(hex.Substring(0, 2), 16);
                    var g = System.Convert.ToByte(hex.Substring(2, 2), 16);
                    var b = System.Convert.ToByte(hex.Substring(4, 2), 16);
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
                }
            }
            catch
            {
                // Fall through to default
            }
        }
        return new SolidColorBrush(Colors.DodgerBlue);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
