using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converts vehicle/firmware type string to appropriate icon bitmap.
/// Uses PNG assets from Assets/Images/Vehicle/ directory.
/// Supports: Copter, Plane, Rover, Heli (Helicopter), Sub, and various frame types.
/// </summary>
public class VehicleTypeToIconConverter : IValueConverter
{
    /// <summary>
    /// Static cache for loaded bitmaps to prevent reloading the same images
    /// </summary>
    private static readonly ConcurrentDictionary<string, Bitmap?> BitmapCache = new();

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
        
        // ArduPilot firmware naming patterns
        ["arducopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["arducopter-heli"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Heli.png",
        ["arduplane"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
        ["ardurover"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Rover.png",
        ["ardusub"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["antennatracker"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Antenna.png",
        ["blimp"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        
        // Plane types
        ["Plane"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
        ["QuadPlane"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
        ["FixedWing"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
        ["Fixed Wing"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
        
        // Rover/Ground types
        ["Rover"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Rover.png",
        ["Ground"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Rover.png",
        
        // Helicopter types
        ["Heli"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Heli.png",
        ["Helicopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Heli.png",
        ["Copter-heli"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Heli.png",
        
        // Multi-rotor types
        ["Hexa"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Hexacopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Octa"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Octocopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        
        // Antenna tracker
        ["AntennaTracker"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Antenna.png",
        ["Tracker"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Antenna.png",
        ["Antenna Tracker"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Antenna.png",
        
        // Submarine/ROV
        ["Sub"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        
        // Additional frame types
        ["Tri"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Tricopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Y6"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["OctaQuad"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["X8"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Single"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Coax"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
        ["Deca"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Decacopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        
        // Unknown/fallback
        ["Unknown"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
    };

    /// <summary>
    /// Default fallback icon when firmware type is unknown
    /// </summary>
    private const string DefaultIcon = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string vehicleType || string.IsNullOrWhiteSpace(vehicleType))
            return GetCachedBitmap(DefaultIcon);

        var iconPath = GetIconPath(vehicleType);
        return GetCachedBitmap(iconPath);
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
        
        // Check for ArduPilot naming patterns first (e.g., arducopter-heli, arduplane)
        if (lowerType.Contains("heli"))
            return VehicleIconMap["Heli"];
        if (lowerType.Contains("plane") || lowerType.Contains("arduplane"))
            return VehicleIconMap["Plane"];
        if (lowerType.Contains("rover") || lowerType.Contains("ground") || lowerType.Contains("ardurover"))
            return VehicleIconMap["Rover"];
        if (lowerType.Contains("antenna") || lowerType.Contains("tracker"))
            return VehicleIconMap["AntennaTracker"];
        if (lowerType.Contains("sub") || lowerType.Contains("ardusub"))
            return VehicleIconMap["Sub"];
        if (lowerType.Contains("hexa") || lowerType.Contains("octa"))
            return VehicleIconMap["Hexa"];
        if (lowerType.Contains("quad") || lowerType.Contains("copter") || lowerType.Contains("arducopter"))
            return VehicleIconMap["Quad"];
        if (lowerType.Contains("blimp"))
            return VehicleIconMap["blimp"];

        // Ultimate fallback
        return DefaultIcon;
    }

    /// <summary>
    /// Gets a cached bitmap or loads it if not cached
    /// </summary>
    private static Bitmap? GetCachedBitmap(string assetPath)
    {
        return BitmapCache.GetOrAdd(assetPath, path =>
        {
            try
            {
                var uri = new Uri(path);
                var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load icon from {path}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// Clears the bitmap cache (useful for testing or memory management)
    /// </summary>
    public static void ClearCache()
    {
        foreach (var bitmap in BitmapCache.Values)
        {
            bitmap?.Dispose();
        }
        BitmapCache.Clear();
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
        
        // ArduPilot firmware naming patterns
        ["arducopter"] = "#667EEA",
        ["arducopter-heli"] = "#F59E0B",
        ["arduplane"] = "#3B82F6",
        ["ardurover"] = "#10B981",
        ["ardusub"] = "#06B6D4",
        ["antennatracker"] = "#EC4899",
        ["blimp"] = "#9CA3AF",
        
        // Plane types
        ["Plane"] = "#3B82F6",        // Blue
        ["QuadPlane"] = "#3B82F6",
        ["FixedWing"] = "#3B82F6",
        ["Fixed Wing"] = "#3B82F6",
        
        // Rover/Ground types
        ["Rover"] = "#10B981",        // Green/Emerald
        ["Ground"] = "#10B981",
        
        // Helicopter types
        ["Heli"] = "#F59E0B",         // Amber/Orange
        ["Helicopter"] = "#F59E0B",
        ["Copter-heli"] = "#F59E0B",
        
        // Multi-rotor types
        ["Hexa"] = "#8B5CF6",         // Violet
        ["Hexacopter"] = "#8B5CF6",
        ["Octa"] = "#8B5CF6",
        ["Octocopter"] = "#8B5CF6",
        
        // Antenna tracker
        ["AntennaTracker"] = "#EC4899", // Pink
        ["Tracker"] = "#EC4899",
        ["Antenna Tracker"] = "#EC4899",
        
        // Submarine/ROV
        ["Sub"] = "#06B6D4",          // Cyan
        
        // Additional frame types
        ["Tri"] = "#667EEA",
        ["Y6"] = "#8B5CF6",
        ["OctaQuad"] = "#8B5CF6",
        ["Single"] = "#667EEA",
        ["Coax"] = "#667EEA",
        ["Deca"] = "#8B5CF6",
        
        // Unknown/fallback
        ["Unknown"] = "#9CA3AF",       // Gray
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
        
        // Check for ArduPilot naming patterns first
        if (lowerType.Contains("heli"))
            return VehicleColorMap["Heli"];
        if (lowerType.Contains("plane") || lowerType.Contains("arduplane"))
            return VehicleColorMap["Plane"];
        if (lowerType.Contains("rover") || lowerType.Contains("ground") || lowerType.Contains("ardurover"))
            return VehicleColorMap["Rover"];
        if (lowerType.Contains("antenna") || lowerType.Contains("tracker"))
            return VehicleColorMap["AntennaTracker"];
        if (lowerType.Contains("sub") || lowerType.Contains("ardusub"))
            return VehicleColorMap["Sub"];
        if (lowerType.Contains("hexa") || lowerType.Contains("octa"))
            return VehicleColorMap["Hexa"];
        if (lowerType.Contains("quad") || lowerType.Contains("copter") || lowerType.Contains("arducopter"))
            return VehicleColorMap["Copter"];
        if (lowerType.Contains("blimp"))
            return VehicleColorMap["blimp"];
        if (lowerType.Contains("unknown"))
            return VehicleColorMap["Unknown"];

        return DefaultColor;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("VehicleTypeToBadgeColorConverter does not support ConvertBack");
    }
}