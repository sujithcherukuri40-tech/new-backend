using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converter that validates parameter values and reverts to default if out of range.
/// Ensures TextBox never displays invalid values.
/// </summary>
public class ParameterValueConverter : IValueConverter
{
    public static readonly ParameterValueConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Convert float to string for display
        if (value is float floatValue)
        {
            return floatValue.ToString("G", culture);
        }
        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Convert string to float, validate, and return
        if (value is string stringValue)
        {
            // Empty string means user cleared the box - return 0
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                return 0f;
            }

            // Try to parse the value
            if (float.TryParse(stringValue, NumberStyles.Float, culture, out float result))
            {
                // Value parsed successfully
                // Validation will happen in DroneParameter.Value setter
                return result;
            }
            else
            {
                // Invalid input (non-numeric) - return original value unchanged
                // This prevents non-numeric characters from being entered
                return Avalonia.Data.BindingNotification.UnsetValue;
            }
        }
        
        return value;
    }
}
