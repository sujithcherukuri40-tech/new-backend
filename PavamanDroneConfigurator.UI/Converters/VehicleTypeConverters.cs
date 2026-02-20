using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Globalization;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converts vehicle type string to appropriate icon path
/// </summary>
public class VehicleTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string vehicleType)
            return GetDefaultIcon();

        var iconPath = vehicleType.ToLowerInvariant() switch
        {
            "copter" or "quadcopter" or "multicopter" => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
            "rover" => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Rover.png",
            "plane" or "fixed wing" or "fixedwing" => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
            "helicopter" or "heli" => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Heli.png",
            "hexacopter" or "hexa" => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
            "tracker" or "antenna tracker" => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Antenna.png",
            _ => GetDefaultIcon()
        };

        try
        {
            var uri = new Uri(iconPath);
            return new Bitmap(AssetLoader.Open(uri));
        }
        catch
        {
            return null;
        }
    }

    private static string GetDefaultIcon() => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts vehicle type to badge background color
/// </summary>
public class VehicleTypeToBadgeColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string vehicleType)
            return new SolidColorBrush(Color.Parse("#667EEA"));

        var color = vehicleType.ToLowerInvariant() switch
        {
            "copter" or "quadcopter" or "multicopter" => "#667EEA",      // Purple
            "rover" => "#10B981",       // Green
            "plane" or "fixed wing" or "fixedwing" => "#3B82F6",       // Blue
            "helicopter" or "heli" => "#F59E0B",  // Orange
            "hexacopter" or "hexa" => "#8B5CF6",        // Violet
            "tracker" or "antenna tracker" => "#06B6D4",         // Cyan
            _ => "#667EEA"
        };

        return new SolidColorBrush(Color.Parse(color));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
