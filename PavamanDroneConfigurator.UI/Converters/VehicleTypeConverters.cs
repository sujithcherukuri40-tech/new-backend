using Avalonia.Data.Converters;
using Avalonia.Media;
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
            "copter" => "avares://PavamanDroneConfigurator.UI/Assets/Vehicles/copter.svg",
            "rover" => "avares://PavamanDroneConfigurator.UI/Assets/Vehicles/rover.svg",
            "plane" => "avares://PavamanDroneConfigurator.UI/Assets/Vehicles/plane.svg",
            "helicopter" => "avares://PavamanDroneConfigurator.UI/Assets/Vehicles/heli.svg",
            "sub" => "avares://PavamanDroneConfigurator.UI/Assets/Vehicles/sub.svg",
            "vtol" => "avares://PavamanDroneConfigurator.UI/Assets/Vehicles/vtol.svg",
            _ => GetDefaultIcon()
        };

        try
        {
            var uri = new Uri(iconPath);
            return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(
                AssetLoader.Open(uri), 48);
        }
        catch
        {
            return null;
        }
    }

    private static string GetDefaultIcon() => "avares://PavamanDroneConfigurator.UI/Assets/Vehicles/copter.svg";

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
            "copter" => "#667EEA",      // Purple
            "rover" => "#10B981",       // Green
            "plane" => "#3B82F6",       // Blue
            "helicopter" => "#F59E0B",  // Orange
            "sub" => "#06B6D4",         // Cyan
            "vtol" => "#8B5CF6",        // Violet
            _ => "#667EEA"
        };

        return new SolidColorBrush(Color.Parse(color));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
