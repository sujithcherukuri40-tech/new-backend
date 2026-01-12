using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converts a boolean value to one of two colors.
/// Use with ConverterParameter="TrueColor|FalseColor" (hex colors with #)
/// Returns Color for use with SolidColorBrush.Color binding
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string paramStr)
        {
            var parts = paramStr.Split('|');
            if (parts.Length == 2)
            {
                var colorStr = boolValue ? parts[0] : parts[1];
                if (Color.TryParse(colorStr, out var color))
                {
                    // Return Color if target expects Color, otherwise return Brush
                    if (targetType == typeof(Color) || targetType == typeof(Color?))
                    {
                        return color;
                    }
                    return new SolidColorBrush(color);
                }
            }
        }
        
        // Return a default color if conversion fails
        if (targetType == typeof(Color) || targetType == typeof(Color?))
        {
            return Colors.Gray;
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts zero to true, non-zero to false (for IsIndeterminate progress)
/// </summary>
public class ZeroToBoolConverter : IValueConverter
{
    public static readonly ZeroToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return d <= 0;
        }
        if (value is int i)
        {
            return i <= 0;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
