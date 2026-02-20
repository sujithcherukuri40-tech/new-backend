# Vehicle Firmware Icon Mapping System

## Overview

This document explains the **vehicle firmware to icon mapping system** used in the Pavaman Drone Configurator. The system automatically displays the correct vehicle icon based on the firmware type selected by the user.

---

## Architecture

### Components

1. **VehicleTypeToIconConverter** - Maps firmware types to PNG icon assets
2. **VehicleTypeToBadgeColorConverter** - Maps firmware types to badge colors
3. **PNG Icon Assets** - Located in `Assets/Images/Vehicle/`
4. **XAML Binding** - Automatic icon updates in UI cards

---

## Icon Assets

### Available Icons

Located in `Assets/Images/Vehicle/`:

| Icon File | Firmware Types | Description |
|-----------|----------------|-------------|
| `Quad.png` | Copter, Quad, Quadcopter, Sub, Tri, Single, Coax | Standard quadcopter icon |
| `QuadPlane.png` | Plane, QuadPlane, FixedWing | Fixed-wing aircraft icon |
| `Rover.png` | Rover, Ground | Ground vehicle icon |
| `Heli.png` | Heli, Helicopter, Copter-heli | Traditional helicopter icon |
| `Hexa.png` | Hexa, Hexacopter, Octa, Octocopter, Y6, OctaQuad, X8, Deca | Multi-rotor icon |
| `Antenna.png` | AntennaTracker, Tracker | Antenna tracker icon |

### Default Fallback

If firmware type is unknown or missing: **`Quad.png`**

---

## Firmware Type Mapping

### 1. VehicleTypeToIconConverter

**Purpose:** Converts firmware type string to bitmap icon

**Code Location:** `PavamanDroneConfigurator.UI\Converters\VehicleTypeConverters.cs`

#### Mapping Logic

```csharp
private static readonly Dictionary<string, string> VehicleIconMap = new()
{
    // Primary vehicle types
    ["Copter"] = "avares://.../Vehicle/Quad.png",
    ["Plane"] = "avares://.../Vehicle/QuadPlane.png",
    ["Rover"] = "avares://.../Vehicle/Rover.png",
    ["Heli"] = "avares://.../Vehicle/Heli.png",
    ["Hexa"] = "avares://.../Vehicle/Hexa.png",
    ["AntennaTracker"] = "avares://.../Vehicle/Antenna.png",
    
    // Aliases and frame types
    ["Quad"] = "avares://.../Vehicle/Quad.png",
    ["Helicopter"] = "avares://.../Vehicle/Heli.png",
    // ... more aliases
};
```

#### Features

? **Case-insensitive lookup** - "Copter", "copter", "COPTER" all work  
? **Keyword fallback** - If exact match fails, searches for keywords  
? **Default fallback** - Always returns Quad.png if unknown  
? **Error handling** - Gracefully handles missing assets  
? **Performance** - Dictionary lookup is O(1)

---

### 2. VehicleTypeToBadgeColorConverter

**Purpose:** Converts firmware type to color-coded badge background

**Code Location:** Same file as icon converter

#### Color Palette

```csharp
private static readonly Dictionary<string, string> VehicleColorMap = new()
{
    ["Copter"] = "#667EEA",      // Purple/Indigo
    ["Plane"] = "#3B82F6",       // Blue
    ["Rover"] = "#10B981",       // Green/Emerald
    ["Heli"] = "#F59E0B",        // Amber/Orange
    ["Hexa"] = "#8B5CF6",        // Violet
    ["AntennaTracker"] = "#EC4899", // Pink
    ["Sub"] = "#06B6D4",         // Cyan
};
```

#### Visual Preview

- ?? **Copter** - Purple (#667EEA)
- ?? **Plane** - Blue (#3B82F6)
- ?? **Rover** - Green (#10B981)
- ?? **Heli** - Amber (#F59E0B)
- ?? **Hexa** - Violet (#8B5CF6)
- ?? **AntennaTracker** - Pink (#EC4899)
- ?? **Sub** - Cyan (#06B6D4)

---

## Usage Examples

### Example 1: Firmware Management Card (XAML)

```xml
<UserControl xmlns:converters="using:PavamanDroneConfigurator.UI.Converters">
    
    <UserControl.Resources>
        <converters:VehicleTypeToIconConverter x:Key="VehicleTypeToIconConverter"/>
        <converters:VehicleTypeToBadgeColorConverter x:Key="VehicleTypeToBadgeColorConverter"/>
    </UserControl.Resources>
    
    <Border>
        <!-- Vehicle Icon -->
        <Image Source="{Binding VehicleType, Converter={StaticResource VehicleTypeToIconConverter}}"
               Width="64" 
               Height="64"/>
        
        <!-- Vehicle Type Badge -->
        <Border Background="{Binding VehicleType, Converter={StaticResource VehicleTypeToBadgeColorConverter}}"
                CornerRadius="8"
                Padding="12,6">
            <TextBlock Text="{Binding VehicleType}"
                       Foreground="White"
                       FontWeight="Bold"/>
        </Border>
    </Border>
</UserControl>
```

### Example 2: Code-Behind Usage

```csharp
using PavamanDroneConfigurator.UI.Converters;

// Get icon path programmatically
var firmwareType = "Copter";
var iconPath = VehicleTypeToIconConverter.GetIconPath(firmwareType);
// Returns: "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png"

// Get badge color programmatically
var badgeColor = VehicleTypeToBadgeColorConverter.GetBadgeColor(firmwareType);
// Returns: "#667EEA"
```

### Example 3: Dynamic Card Update

```csharp
// ViewModel property
public string VehicleType { get; set; } = "Copter";

// When firmware changes, just update the property
// The converter automatically updates the icon
VehicleType = "Plane"; // Icon changes to QuadPlane.png
VehicleType = "Rover"; // Icon changes to Rover.png
```

---

## Extending the System

### Adding a New Firmware Type

**Step 1:** Add the PNG icon to `Assets/Images/Vehicle/`

```
Assets/Images/Vehicle/
??? NewVehicle.png  ? NEW
```

**Step 2:** Update the mapping in `VehicleTypeConverters.cs`

```csharp
private static readonly Dictionary<string, string> VehicleIconMap = new()
{
    // ... existing mappings
    
    // ? ADD NEW MAPPING
    ["NewVehicle"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/NewVehicle.png",
    ["NewAlias"] = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/NewVehicle.png",
};
```

**Step 3:** (Optional) Add badge color

```csharp
private static readonly Dictionary<string, string> VehicleColorMap = new()
{
    // ... existing colors
    
    // ? ADD NEW COLOR
    ["NewVehicle"] = "#FF6B6B", // Red
};
```

**Step 4:** Rebuild the application

```bash
dotnet build
```

---

## Current Firmware Support

### Supported Vehicle Types

| Firmware Type | Icon | Badge Color | Notes |
|--------------|------|-------------|-------|
| Copter | Quad.png | Purple (#667EEA) | Standard quadcopter |
| Quad | Quad.png | Purple | Alias for Copter |
| Plane | QuadPlane.png | Blue (#3B82F6) | Fixed-wing |
| QuadPlane | QuadPlane.png | Blue | VTOL aircraft |
| Rover | Rover.png | Green (#10B981) | Ground vehicle |
| Heli | Heli.png | Amber (#F59E0B) | Traditional helicopter |
| Copter-heli | Heli.png | Amber | Helicopter firmware variant |
| Hexa | Hexa.png | Violet (#8B5CF6) | Hexacopter |
| Octa | Hexa.png | Violet | Octocopter |
| AntennaTracker | Antenna.png | Pink (#EC4899) | Antenna tracker |
| Sub | Quad.png | Cyan (#06B6D4) | Underwater ROV |
| Tri | Quad.png | Purple | Tricopter |
| Y6 | Hexa.png | Violet | Y6 configuration |
| OctaQuad | Hexa.png | Violet | X8 / OctaQuad |
| Single | Quad.png | Purple | Single rotor |
| Coax | Quad.png | Purple | Coaxial helicopter |
| Deca | Hexa.png | Violet | Decacopter (10 motors) |

### Fallback Behavior

```
Unknown Type ? Check Keywords ? Default to Quad.png
```

**Keyword Search:**
- Contains "copter" or "quad" ? Quad.png
- Contains "plane" ? QuadPlane.png
- Contains "rover" or "ground" ? Rover.png
- Contains "heli" ? Heli.png
- Contains "hexa" or "octa" ? Hexa.png
- Contains "antenna" or "tracker" ? Antenna.png

---

## Technical Details

### Asset Loading

Icons are loaded as **embedded resources** using Avalonia's asset system:

```
avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png
```

### Performance

- **Lookup:** O(1) dictionary lookup
- **Caching:** Avalonia automatically caches loaded bitmaps
- **Memory:** Icons loaded on-demand, not all at once

### Error Handling

```csharp
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
        // Logs error and returns null
        // UI will show blank instead of crashing
        return null;
    }
}
```

---

## Best Practices

### ? DO

- Use case-insensitive firmware type strings
- Provide aliases for common variations
- Test with unknown firmware types
- Add tooltips showing firmware type
- Use consistent icon sizes (64×64px recommended)

### ? DON'T

- Hard-code icon paths in XAML
- Use file system paths (use asset URIs)
- Assume firmware type is always valid
- Skip fallback icons
- Use different icon formats (stick to PNG)

---

## Troubleshooting

### Icon Not Showing

**Problem:** Icon displays as blank/null

**Solutions:**
1. Check asset path spelling
2. Verify PNG file exists in `Assets/Images/Vehicle/`
3. Ensure asset is marked as `AvaloniaResource` in .csproj
4. Check firmware type spelling

### Wrong Icon Displayed

**Problem:** Incorrect icon for firmware type

**Solutions:**
1. Verify firmware type string value
2. Check mapping in `VehicleIconMap`
3. Add specific mapping if missing
4. Test keyword fallback logic

### Badge Color Not Showing

**Problem:** Badge shows default purple color

**Solutions:**
1. Check `VehicleColorMap` for firmware type
2. Add color mapping if missing
3. Verify converter is registered in XAML resources

---

## Examples in Codebase

### Current Usage Locations

1. **FirmwareManagementPage.axaml** - Admin firmware card display
2. **FirmwareWindow.axaml** - Firmware flashing UI
3. **AdminDashboardWindow.axaml** - Dashboard statistics

### Sample Code References

**XAML:**
```xml
<Image Source="{Binding VehicleType, Converter={StaticResource VehicleTypeToIconConverter}}"
       Width="64" Height="64"/>
```

**ViewModel:**
```csharp
public string VehicleType { get; set; } = "Copter";
```

The converter handles everything automatically!

---

## Future Enhancements

### Planned Improvements

1. **SVG Support** - Add vector graphics for scaling
2. **Theme Support** - Dark mode icon variants
3. **Custom Icons** - Allow user-uploaded icons
4. **Animated Icons** - Subtle animations on hover
5. **Icon Previews** - Admin panel icon library

### Adding SVG Support

```csharp
// Future implementation
private static SvgImage? LoadSvgIcon(string assetPath)
{
    // Load SVG instead of PNG
    // Allows infinite scaling without quality loss
}
```

---

## Summary

### Key Benefits

? **Maintainable** - Centralized mapping in one file  
? **Scalable** - Easy to add new firmware types  
? **Robust** - Multiple fallback strategies  
? **Performance** - O(1) lookups with caching  
? **User-Friendly** - Automatic UI updates  
? **Type-Safe** - Compile-time asset validation  

### Quick Reference

| Component | Purpose | Location |
|-----------|---------|----------|
| `VehicleTypeToIconConverter` | Maps firmware ? icon | `Converters/VehicleTypeConverters.cs` |
| `VehicleTypeToBadgeColorConverter` | Maps firmware ? color | Same file |
| PNG Assets | Icon images | `Assets/Images/Vehicle/` |
| XAML Binding | UI integration | `{Binding VehicleType, Converter=...}` |

---

**Version:** 1.0  
**Last Updated:** 2025-01-27  
**Maintained By:** Pavaman Development Team
