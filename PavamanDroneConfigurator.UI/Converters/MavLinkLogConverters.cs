using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PavamanDroneConfigurator.Infrastructure.Services;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converts MAVLink message direction to background color
/// Outgoing (TX) messages show green tint, Incoming (RX) messages show blue tint
/// </summary>
public class DirectionToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MavLinkDirection direction)
        {
            return direction == MavLinkDirection.Outgoing 
                ? new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7)) // Green tint (#DCFCE7)
                : new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE)); // Blue tint (#DBEAFE)
        }
        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts MAVLink message direction to prefix text (TX → or RX ←)
/// </summary>
public class DirectionToPrefixConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MavLinkDirection direction)
        {
            return direction == MavLinkDirection.Outgoing ? "TX →" : "RX ←";
        }
        return "??";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
