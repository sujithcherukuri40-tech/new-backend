using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converts calibration image asset URI string to a Bitmap.
/// Caches loaded bitmaps for performance.
/// </summary>
public class CalibrationImageConverter : IValueConverter
{
    /// <summary>
    /// Static cache for loaded bitmaps to prevent reloading the same images
    /// </summary>
    private static readonly ConcurrentDictionary<string, Bitmap?> BitmapCache = new();

    /// <summary>
    /// Default fallback image when no image path is provided
    /// </summary>
    private const string DefaultImage = "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Level.png";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string assetPath || string.IsNullOrWhiteSpace(assetPath))
        {
            return GetCachedBitmap(DefaultImage);
        }

        return GetCachedBitmap(assetPath);
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
                System.Diagnostics.Debug.WriteLine($"Failed to load calibration image from {path}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// Clears the bitmap cache (useful for memory management)
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
        throw new NotSupportedException("CalibrationImageConverter does not support ConvertBack");
    }
}
