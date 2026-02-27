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
    
    /// <summary>
    /// Color to use when value is true (for use without ConverterParameter)
    /// </summary>
    public string TrueColor { get; set; } = "#10B981";
    
    /// <summary>
    /// Color to use when value is false (for use without ConverterParameter)
    /// </summary>
    public string FalseColor { get; set; } = "#EF4444";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        
        string? trueColorStr = null;
        string? falseColorStr = null;
        
        // Check for parameter first (takes precedence)
        if (parameter is string paramStr && paramStr.Contains('|'))
        {
            var parts = paramStr.Split('|');
            if (parts.Length == 2)
            {
                trueColorStr = parts[0];
                falseColorStr = parts[1];
            }
        }
        
        // Fall back to properties if no parameter
        trueColorStr ??= TrueColor;
        falseColorStr ??= FalseColor;
        
        var colorStr = boolValue ? trueColorStr : falseColorStr;
        
        if (Color.TryParse(colorStr, out var color))
        {
            // Return Color if target expects Color, otherwise return Brush
            if (targetType == typeof(Color) || targetType == typeof(Color?))
            {
                return color;
            }
            return new SolidColorBrush(color);
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
/// Converts a boolean value to one of two background brushes.
/// Use with ConverterParameter="TrueColor|FalseColor" (hex colors with #)
/// Always returns IBrush for Background property binding.
/// </summary>
public class BoolToBackgroundConverter : IValueConverter
{
    public static readonly BoolToBackgroundConverter Instance = new();
    
    /// <summary>
    /// Background color when value is true
    /// </summary>
    public string TrueBackground { get; set; } = "#DCFCE7";
    
    /// <summary>
    /// Background color when value is false
    /// </summary>
    public string FalseBackground { get; set; } = "#F3F4F6";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        
        string? trueColorStr = null;
        string? falseColorStr = null;
        
        // Check for parameter first (takes precedence)
        if (parameter is string paramStr && paramStr.Contains('|'))
        {
            var parts = paramStr.Split('|');
            if (parts.Length == 2)
            {
                trueColorStr = parts[0];
                falseColorStr = parts[1];
            }
        }
        
        // Fall back to properties if no parameter
        trueColorStr ??= TrueBackground;
        falseColorStr ??= FalseBackground;
        
        var colorStr = boolValue ? trueColorStr : falseColorStr;
        
        if (Color.TryParse(colorStr, out var color))
        {
            return new SolidColorBrush(color);
        }
        
        return Brushes.LightGray;
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
