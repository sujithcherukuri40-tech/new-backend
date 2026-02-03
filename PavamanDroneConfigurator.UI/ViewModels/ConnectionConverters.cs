using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PavamanDroneConfigurator.Core.Enums;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// Converts ConnectionType enum to ComboBox index and vice versa.
/// </summary>
public class ConnectionTypeToIndexConverter : IValueConverter
{
    public static readonly ConnectionTypeToIndexConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConnectionType type)
        {
            return type switch
            {
                ConnectionType.Serial => 0,
                ConnectionType.Tcp => 1,
                ConnectionType.Bluetooth => 2,
                _ => 0
            };
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index)
        {
            return index switch
            {
                0 => ConnectionType.Serial,
                1 => ConnectionType.Tcp,
                2 => ConnectionType.Bluetooth,
                _ => ConnectionType.Serial
            };
        }
        return ConnectionType.Serial;
    }
}
