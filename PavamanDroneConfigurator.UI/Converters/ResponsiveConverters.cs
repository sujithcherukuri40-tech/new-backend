using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Responsive breakpoint definitions for adaptive layouts.
/// Matches common device categories across platforms.
/// </summary>
public static class ResponsiveBreakpoints
{
    // Width breakpoints
    public const double MobileMaxWidth = 600;
    public const double TabletMaxWidth = 1024;
    public const double DesktopMinWidth = 1025;
    public const double LargeDesktopMinWidth = 1440;
    
    // Height breakpoints
    public const double CompactMaxHeight = 600;
    public const double StandardMaxHeight = 900;
    
    /// <summary>
    /// Gets the current device form factor based on width.
    /// </summary>
    public static FormFactor GetFormFactor(double width)
    {
        return width switch
        {
            <= MobileMaxWidth => FormFactor.Mobile,
            <= TabletMaxWidth => FormFactor.Tablet,
            <= LargeDesktopMinWidth => FormFactor.Desktop,
            _ => FormFactor.LargeDesktop
        };
    }
    
    /// <summary>
    /// Gets whether the layout should be compact based on height.
    /// </summary>
    public static bool IsCompactHeight(double height) => height <= CompactMaxHeight;
}

/// <summary>
/// Device form factor categories.
/// </summary>
public enum FormFactor
{
    Mobile,      // < 600px width
    Tablet,      // 600-1024px width  
    Desktop,     // 1025-1440px width
    LargeDesktop // > 1440px width
}

/// <summary>
/// Converts window width to a boolean for responsive visibility.
/// Shows/hides elements based on width threshold.
/// Usage: {Binding $parent[Window].Bounds.Width, Converter={StaticResource WidthToVisibilityConverter}, ConverterParameter=1024}
/// </summary>
public class WidthToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// If true, element is visible when width is ABOVE threshold.
    /// If false, element is visible when width is BELOW threshold.
    /// </summary>
    public bool ShowAboveThreshold { get; set; } = true;
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width)
            return true;
            
        double threshold = 1024;
        if (parameter is string paramStr && double.TryParse(paramStr, out var parsed))
            threshold = parsed;
        else if (parameter is double paramDouble)
            threshold = paramDouble;
            
        return ShowAboveThreshold ? width >= threshold : width < threshold;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts window dimensions to number of grid columns.
/// Usage: {Binding $parent[Window].Bounds.Width, Converter={StaticResource WidthToColumnsConverter}}
/// </summary>
public class WidthToColumnsConverter : IValueConverter
{
    public int MobileColumns { get; set; } = 1;
    public int TabletColumns { get; set; } = 2;
    public int DesktopColumns { get; set; } = 3;
    public int LargeDesktopColumns { get; set; } = 4;
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width)
            return DesktopColumns;
            
        return ResponsiveBreakpoints.GetFormFactor(width) switch
        {
            FormFactor.Mobile => MobileColumns,
            FormFactor.Tablet => TabletColumns,
            FormFactor.Desktop => DesktopColumns,
            FormFactor.LargeDesktop => LargeDesktopColumns,
            _ => DesktopColumns
        };
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts window width to responsive font size.
/// Usage: {Binding $parent[Window].Bounds.Width, Converter={StaticResource WidthToFontSizeConverter}}
/// </summary>
public class WidthToFontSizeConverter : IValueConverter
{
    public double MobileFontSize { get; set; } = 12;
    public double TabletFontSize { get; set; } = 13;
    public double DesktopFontSize { get; set; } = 14;
    public double LargeDesktopFontSize { get; set; } = 15;
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width)
            return DesktopFontSize;
            
        // Allow parameter to specify a scale factor
        double scale = 1.0;
        if (parameter is string paramStr && double.TryParse(paramStr, out var parsed))
            scale = parsed;
            
        var baseSize = ResponsiveBreakpoints.GetFormFactor(width) switch
        {
            FormFactor.Mobile => MobileFontSize,
            FormFactor.Tablet => TabletFontSize,
            FormFactor.Desktop => DesktopFontSize,
            FormFactor.LargeDesktop => LargeDesktopFontSize,
            _ => DesktopFontSize
        };
        
        return baseSize * scale;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts window width to responsive padding/margin.
/// Usage: {Binding $parent[Window].Bounds.Width, Converter={StaticResource WidthToPaddingConverter}}
/// </summary>
public class WidthToPaddingConverter : IValueConverter
{
    public Thickness MobilePadding { get; set; } = new(8);
    public Thickness TabletPadding { get; set; } = new(12);
    public Thickness DesktopPadding { get; set; } = new(16);
    public Thickness LargeDesktopPadding { get; set; } = new(24);
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width)
            return DesktopPadding;
            
        return ResponsiveBreakpoints.GetFormFactor(width) switch
        {
            FormFactor.Mobile => MobilePadding,
            FormFactor.Tablet => TabletPadding,
            FormFactor.Desktop => DesktopPadding,
            FormFactor.LargeDesktop => LargeDesktopPadding,
            _ => DesktopPadding
        };
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts window width to StackPanel orientation (vertical on mobile, horizontal on larger screens).
/// Usage: {Binding $parent[Window].Bounds.Width, Converter={StaticResource WidthToOrientationConverter}}
/// </summary>
public class WidthToOrientationConverter : IValueConverter
{
    public double HorizontalThreshold { get; set; } = 600;
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width)
            return Orientation.Horizontal;
            
        return width >= HorizontalThreshold ? Orientation.Horizontal : Orientation.Vertical;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts window width to sidebar width (collapsed on mobile, expanded on desktop).
/// Usage: {Binding $parent[Window].Bounds.Width, Converter={StaticResource WidthToSidebarWidthConverter}}
/// </summary>
public class WidthToSidebarWidthConverter : IValueConverter
{
    public double CollapsedWidth { get; set; } = 60;
    public double ExpandedWidth { get; set; } = 240;
    public double CollapseThreshold { get; set; } = 800;
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width)
            return ExpandedWidth;
            
        return width >= CollapseThreshold ? ExpandedWidth : CollapsedWidth;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts window width to grid column definitions string.
/// Usage: Cannot be used directly in XAML ColumnDefinitions - use ResponsiveGrid control instead.
/// </summary>
public class WidthToColumnDefinitionsConverter : IValueConverter
{
    public string MobileColumns { get; set; } = "*";
    public string TabletColumns { get; set; } = "*,*";
    public string DesktopColumns { get; set; } = "*,*,*";
    public string LargeDesktopColumns { get; set; } = "*,*,*,*";
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width)
            return DesktopColumns;
            
        return ResponsiveBreakpoints.GetFormFactor(width) switch
        {
            FormFactor.Mobile => MobileColumns,
            FormFactor.Tablet => TabletColumns,
            FormFactor.Desktop => DesktopColumns,
            FormFactor.LargeDesktop => LargeDesktopColumns,
            _ => DesktopColumns
        };
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts window Bounds to specific dimension (Width or Height).
/// Usage: {Binding $parent[Window].Bounds, Converter={StaticResource BoundsToWidthConverter}}
/// </summary>
public class BoundsToWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Rect bounds)
            return bounds.Width;
        return 1024.0; // Default width
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts window Bounds to Height.
/// </summary>
public class BoundsToHeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Rect bounds)
            return bounds.Height;
        return 768.0; // Default height
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts window width to FormFactor enum for advanced scenarios.
/// Usage: {Binding $parent[Window].Bounds.Width, Converter={StaticResource WidthToFormFactorConverter}}
/// </summary>
public class WidthToFormFactorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width)
            return FormFactor.Desktop;
            
        return ResponsiveBreakpoints.GetFormFactor(width);
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Multi-value converter for complex responsive scenarios.
/// Takes Width and Height and returns a value based on both dimensions.
/// </summary>
public class ResponsiveMultiConverter : IMultiValueConverter
{
    public object? MobileValue { get; set; }
    public object? TabletValue { get; set; }
    public object? DesktopValue { get; set; }
    public object? CompactValue { get; set; } // Used when height is small
    
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return DesktopValue;
            
        var width = values[0] as double? ?? 1024;
        var height = values[1] as double? ?? 768;
        
        // If compact height, use compact value if available
        if (ResponsiveBreakpoints.IsCompactHeight(height) && CompactValue != null)
            return CompactValue;
            
        return ResponsiveBreakpoints.GetFormFactor(width) switch
        {
            FormFactor.Mobile => MobileValue ?? TabletValue ?? DesktopValue,
            FormFactor.Tablet => TabletValue ?? DesktopValue,
            _ => DesktopValue
        };
    }
}

/// <summary>
/// Converts window width to item width for wrap panels (responsive card sizing).
/// </summary>
public class WidthToItemWidthConverter : IValueConverter
{
    public double MinItemWidth { get; set; } = 280;
    public double MaxItemWidth { get; set; } = 400;
    public double ItemSpacing { get; set; } = 16;
    public int TargetColumnsOnDesktop { get; set; } = 3;
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double containerWidth)
            return MinItemWidth;
            
        // Calculate optimal item width based on container width
        var availableWidth = containerWidth - ItemSpacing;
        var formFactor = ResponsiveBreakpoints.GetFormFactor(containerWidth);
        
        int targetColumns = formFactor switch
        {
            FormFactor.Mobile => 1,
            FormFactor.Tablet => 2,
            FormFactor.Desktop => TargetColumnsOnDesktop,
            FormFactor.LargeDesktop => TargetColumnsOnDesktop + 1,
            _ => TargetColumnsOnDesktop
        };
        
        var itemWidth = (availableWidth - (ItemSpacing * (targetColumns - 1))) / targetColumns;
        
        // Clamp to min/max bounds
        return Math.Clamp(itemWidth, MinItemWidth, MaxItemWidth);
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
