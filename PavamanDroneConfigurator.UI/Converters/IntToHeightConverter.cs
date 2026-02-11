using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converts an integer value to a height for bar charts.
/// Scales the value to fit within a maximum height.
/// </summary>
public class IntToHeightConverter : IValueConverter
{
    public static readonly IntToHeightConverter Instance = new();
    
    private const double MaxHeight = 100.0;
    private const double MaxValue = 100.0;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            // Scale the value to fit within MaxHeight
            var scaledHeight = Math.Min((intValue / MaxValue) * MaxHeight, MaxHeight);
            return Math.Max(scaledHeight, 4); // Minimum 4px height
        }
        return 4.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
