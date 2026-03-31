using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converts a PWM value (800-2200) to a stick-indicator position within a fixed-size area.
/// ConverterParameter supplies the available canvas size (e.g. "120").
/// The result is the offset of the indicator centre (0 … canvasSize).
/// </summary>
public class PwmToStickPositionConverter : IValueConverter
{
    private const double PwmMin = 800;
    private const double PwmMax = 2200;
    private const double PwmRange = PwmMax - PwmMin;

    public static readonly PwmToStickPositionConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pwm = value switch
        {
            int i => i,
            double d => d,
            _ => 1500
        };

        double canvasSize = 120;
        if (parameter is string s && double.TryParse(s, CultureInfo.InvariantCulture, out var parsed))
            canvasSize = parsed;

        // Clamp PWM into valid range
        pwm = Math.Clamp(pwm, PwmMin, PwmMax);

        // Normalise 0..1
        double normalised = (pwm - PwmMin) / PwmRange;

        return normalised * canvasSize;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Same as <see cref="PwmToStickPositionConverter"/> but inverts the axis
/// (useful for Pitch / Throttle where stick-up = high PWM but visual Y increases downward).
/// </summary>
public class PwmToStickPositionInvertedConverter : IValueConverter
{
    private const double PwmMin = 800;
    private const double PwmMax = 2200;
    private const double PwmRange = PwmMax - PwmMin;

    public static readonly PwmToStickPositionInvertedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pwm = value switch
        {
            int i => i,
            double d => d,
            _ => 1500
        };

        double canvasSize = 120;
        if (parameter is string s && double.TryParse(s, CultureInfo.InvariantCulture, out var parsed))
            canvasSize = parsed;

        pwm = Math.Clamp(pwm, PwmMin, PwmMax);
        double normalised = (pwm - PwmMin) / PwmRange;

        // Invert: high PWM → low Y position
        return (1.0 - normalised) * canvasSize;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
