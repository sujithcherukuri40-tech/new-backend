using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converts vehicle/firmware type string to appropriate icon path.
/// Uses PNG assets from Assets/Images/Vehicle/ directory.
/// Supports: Copter, Plane, Rover, Heli (Helicopter), Sub, and various frame types.
/// </summary>
public class VehicleTypeToIconConverter : IValueConverter
{
    /// <summary>
    /// Firmware type to icon mapping.
    /// Key = firmware type (case-insensitive), Value = asset path
    /// </summary>
    private static readonly Dictionary<string, string> VehicleIconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Primary vehicle types
        ["Copter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Quad"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Quadcopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Multicopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Plane"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
        ["QuadPlane"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
        ["FixedWing"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
        ["Fixed Wing"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
        ["Rover"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Rover.png",
        ["Ground"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Rover.png",
        ["Heli"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Heli.png",
        ["Helicopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Heli.png",
        ["Copter-heli"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Heli.png",
        ["Hexa"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Hexacopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Octa"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Octocopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["AntennaTracker"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Antenna.png",
        ["Tracker"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Antenna.png",
        ["Antenna Tracker"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Antenna.png",
        // Additional frame types (fallback to Quad for now)
        ["Sub"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Tri"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Tricopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Y6"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["OctaQuad"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["X8"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Single"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Coax"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Deca"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Decacopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
    };

    /// <summary>
    /// Default fallback icon when firmware type is unknown
    /// </summary>
    private const string DefaultIcon = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string vehicleType || string.IsNullOrWhiteSpace(vehicleType))
            return LoadIcon(DefaultIcon);

        var iconPath = GetIconPath(vehicleType);
        return LoadIcon(iconPath);
    }

    /// <summary>
    /// Gets the icon path for a given vehicle type.
    /// Supports case-insensitive lookup with fallback to default.
    /// </summary>
    /// <param name="vehicleType">Vehicle/firmware type string</param>
    /// <returns>Asset URI path to the icon</returns>
    public static string GetIconPath(string vehicleType)
    {
        if (string.IsNullOrWhiteSpace(vehicleType))
            return DefaultIcon;

        // Direct lookup
        if (VehicleIconMap.TryGetValue(vehicleType, out var iconPath))
            return iconPath;

        // Fallback: check if the vehicle type contains known keywords
        var lowerType = vehicleType.ToLowerInvariant();
        if (lowerType.Contains("quad") || lowerType.Contains("copter"))
            return VehicleIconMap["Quad"];
        if (lowerType.Contains("plane"))
            return VehicleIconMap["Plane"];
        if (lowerType.Contains("rover") || lowerType.Contains("ground"))
            return VehicleIconMap["Rover"];
        if (lowerType.Contains("heli"))
            return VehicleIconMap["Heli"];
        if (lowerType.Contains("hexa") || lowerType.Contains("octa"))
            return VehicleIconMap["Hexa"];
        if (lowerType.Contains("antenna") || lowerType.Contains("tracker"))
            return VehicleIconMap["AntennaTracker"];

        // Ultimate fallback
        return DefaultIcon;
    }

    /// <summary>
    /// Loads a bitmap from the asset path with error handling
    /// </summary>
    private static Bitmap? LoadIcon(string assetPath)
    {
        try
        {
            var uri = new Uri(assetPath);
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            // Log error in production code
            System.Diagnostics.Debug.WriteLine($"Failed to load icon from {assetPath}: {ex.Message}");
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("VehicleTypeToIconConverter does not support ConvertBack");
    }
}

/// <summary>
/// Converts vehicle type to badge background color.
/// Provides distinct, professional colors for each vehicle type.
/// </summary>
public class VehicleTypeToBadgeColorConverter : IValueConverter
{
    /// <summary>
    /// Vehicle type to color mapping.
    /// Uses soft, modern colors suitable for badges.
    /// </summary>
    private static readonly Dictionary<string, string> VehicleColorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Primary vehicle types
        ["Copter"] = "#667EEA",       // Purple/Indigo
        ["Quad"] = "#667EEA",
        ["Quadcopter"] = "#667EEA",
        ["Multicopter"] = "#667EEA",
        ["Plane"] = "#3B82F6",        // Blue
        ["QuadPlane"] = "#3B82F6",
        ["FixedWing"] = "#3B82F6",
        ["Fixed Wing"] = "#3B82F6",
        ["Rover"] = "#10B981",        // Green/Emerald
        ["Ground"] = "#10B981",
        ["Heli"] = "#F59E0B",         // Amber/Orange
        ["Helicopter"] = "#F59E0B",
        ["Copter-heli"] = "#F59E0B",
        ["Hexa"] = "#8B5CF6",         // Violet
        ["Hexacopter"] = "#8B5CF6",
        ["Octa"] = "#8B5CF6",
        ["Octocopter"] = "#8B5CF6",
        ["AntennaTracker"] = "#EC4899", // Pink
        ["Tracker"] = "#EC4899",
        ["Antenna Tracker"] = "#06B6D4",
        ["Sub"] = "#06B6D4",          // Cyan
        // Additional frame types
        ["Tri"] = "#667EEA",
        ["Y6"] = "#8B5CF6",
        ["OctaQuad"] = "#8B5CF6",
        ["Single"] = "#667EEA",
        ["Coax"] = "#667EEA",
        ["Deca"] = "#8B5CF6",
    };

    /// <summary>
    /// Default fallback color
    /// </summary>
    private const string DefaultColor = "#667EEA";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string vehicleType || string.IsNullOrWhiteSpace(vehicleType))
            return new SolidColorBrush(Color.Parse(DefaultColor));

        var colorHex = GetBadgeColor(vehicleType);
        return new SolidColorBrush(Color.Parse(colorHex));
    }

    /// <summary>
    /// Gets the badge color for a given vehicle type
    /// </summary>
    public static string GetBadgeColor(string vehicleType)
    {
        if (string.IsNullOrWhiteSpace(vehicleType))
            return DefaultColor;

        // Direct lookup
        if (VehicleColorMap.TryGetValue(vehicleType, out var color))
            return color;

        // Fallback: check if the vehicle type contains known keywords
        var lowerType = vehicleType.ToLowerInvariant();
        if (lowerType.Contains("quad") || lowerType.Contains("copter"))
            return VehicleColorMap["Copter"];
        if (lowerType.Contains("plane"))
            return VehicleColorMap["Plane"];
        if (lowerType.Contains("rover") || lowerType.Contains("ground"))
            return VehicleColorMap["Rover"];
        if (lowerType.Contains("heli"))
            return VehicleColorMap["Heli"];
        if (lowerType.Contains("hexa") || lowerType.Contains("octa"))
            return VehicleColorMap["Hexa"];
        if (lowerType.Contains("antenna") || lowerType.Contains("tracker"))
            return VehicleColorMap["AntennaTracker"];
        if (lowerType.Contains("sub"))
            return VehicleColorMap["Sub"];

        return DefaultColor;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("VehicleTypeToBadgeColorConverter does not support ConvertBack");
    }
}