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
        
        // Multi-rotor types - Hexa
        ["Hexa"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        ["Hexacopter"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
        
        // Multi-rotor types - Octa
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
    public static string GetIconPath(string vehicleType)
    {
        if (string.IsNullOrWhiteSpace(vehicleType))
            return DefaultIcon;

        // Direct lookup
        if (VehicleIconMap.TryGetValue(vehicleType, out var iconPath))
            return iconPath;

        // Fallback: check if the vehicle type contains known keywords
        var lowerType = vehicleType.ToLowerInvariant();
        
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

        return DefaultIcon;
    }

    /// <summary>
    /// Gets a cached bitmap or loads it if not cached
    /// </summary>
    public static Bitmap? GetCachedBitmap(string assetPath)
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
                System.Diagnostics.Debug.WriteLine("Failed to load icon from " + path + ": " + ex.Message);
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
/// Converts frame class and frame type to appropriate icon bitmap for airframe configuration.
/// Uses specific frame configuration images (quad-x, quad-plus, hexa-x, etc.)
/// </summary>
public class FrameConfigToIconConverter : IMultiValueConverter
{
    /// <summary>
    /// Frame class values to base names
    /// </summary>
    private static readonly Dictionary<int, string> FrameClassNames = new()
    {
        [0] = "quad",
        [1] = "quad",
        [2] = "hexa",
        [3] = "octa",
        [4] = "octaquad",
        [5] = "hexa",
        [7] = "quad",
        [10] = "quad",
        [13] = "quad",
    };
    
    /// <summary>
    /// Frame type values to suffix names
    /// </summary>
    private static readonly Dictionary<int, string> FrameTypeNames = new()
    {
        [0] = "plus",
        [1] = "x",
        [2] = "v",
        [3] = "h",
        [4] = "vtail",
        [5] = "atail",
    };

    /// <summary>
    /// Known existing file combinations
    /// </summary>
    private static readonly HashSet<string> KnownFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "quad-plus", "quad-x", "quad-v", "quad-h", "quad-vtail", "quad-atail",
        "hexa-plus", "hexa-x", "hexa-v", "hexa-h",
        "octa-plus", "octa-x", "octa-v", "octa-h",
        "octaquad-plus", "octaquad-x", "octaquad-v",
    };

    private const string DefaultIcon = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png";

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 2)
        {
            return VehicleTypeToIconConverter.GetCachedBitmap(DefaultIcon);
        }

        int frameClass = 1;
        int frameType = 1;
        
        if (values[0] is int fc)
            frameClass = fc;
        else if (values[0] is double dfc)
            frameClass = (int)dfc;
        else if (values[0] is float ffc)
            frameClass = (int)ffc;
            
        if (values[1] is int ft)
            frameType = ft;
        else if (values[1] is double dft)
            frameType = (int)dft;
        else if (values[1] is float fft)
            frameType = (int)fft;

        var iconPath = GetFrameIconPath(frameClass, frameType);
        return VehicleTypeToIconConverter.GetCachedBitmap(iconPath);
    }

    /// <summary>
    /// Gets the icon path for a specific frame class and type combination.
    /// </summary>
    public static string GetFrameIconPath(int frameClass, int frameType)
    {
        var className = FrameClassNames.GetValueOrDefault(frameClass, "quad");
        var typeName = FrameTypeNames.GetValueOrDefault(frameType, "x");
        var fileName = className + "-" + typeName;
        
        if (KnownFiles.Contains(fileName))
        {
            return "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/" + fileName + ".png";
        }
        
        return className switch
        {
            "quad" => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
            "hexa" => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
            "octa" => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
            "octaquad" => "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
            _ => DefaultIcon
        };
    }
}

/// <summary>
/// Converts vehicle type to badge background color.
/// </summary>
public class VehicleTypeToBadgeColorConverter : IValueConverter
{
    private static readonly Dictionary<string, string> VehicleColorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Copter"] = "#667EEA",
        ["Quad"] = "#667EEA",
        ["Quadcopter"] = "#667EEA",
        ["Multicopter"] = "#667EEA",
        ["arducopter"] = "#667EEA",
        ["arducopter-heli"] = "#F59E0B",
        ["arduplane"] = "#3B82F6",
        ["ardurover"] = "#10B981",
        ["ardusub"] = "#06B6D4",
        ["antennatracker"] = "#EC4899",
        ["blimp"] = "#9CA3AF",
        ["Plane"] = "#3B82F6",
        ["QuadPlane"] = "#3B82F6",
        ["FixedWing"] = "#3B82F6",
        ["Fixed Wing"] = "#3B82F6",
        ["Rover"] = "#10B981",
        ["Ground"] = "#10B981",
        ["Heli"] = "#F59E0B",
        ["Helicopter"] = "#F59E0B",
        ["Copter-heli"] = "#F59E0B",
        ["Hexa"] = "#8B5CF6",
        ["Hexacopter"] = "#8B5CF6",
        ["Octa"] = "#8B5CF6",
        ["Octocopter"] = "#8B5CF6",
        ["AntennaTracker"] = "#EC4899",
        ["Tracker"] = "#EC4899",
        ["Antenna Tracker"] = "#EC4899",
        ["Sub"] = "#06B6D4",
        ["Tri"] = "#667EEA",
        ["Y6"] = "#8B5CF6",
        ["OctaQuad"] = "#8B5CF6",
        ["Single"] = "#667EEA",
        ["Coax"] = "#667EEA",
        ["Deca"] = "#8B5CF6",
        ["Unknown"] = "#9CA3AF",
    };

    private const string DefaultColor = "#667EEA";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string vehicleType || string.IsNullOrWhiteSpace(vehicleType))
            return new SolidColorBrush(Color.Parse(DefaultColor));

        var colorHex = GetBadgeColor(vehicleType);
        return new SolidColorBrush(Color.Parse(colorHex));
    }

    public static string GetBadgeColor(string vehicleType)
    {
        if (string.IsNullOrWhiteSpace(vehicleType))
            return DefaultColor;

        if (VehicleColorMap.TryGetValue(vehicleType, out var color))
            return color;

        var lowerType = vehicleType.ToLowerInvariant();
        
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