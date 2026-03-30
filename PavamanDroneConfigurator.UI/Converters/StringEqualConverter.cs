using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Returns true when the bound string equals the ConverterParameter string.
/// Used to drive Classes.active binding from a single string property.
/// Usage: Classes.some-active-class="{Binding StringProp, Converter={x:Static conv:StringEqualConverter.Instance}, ConverterParameter='expected'}"
/// </summary>
public class StringEqualConverter : IValueConverter
{
    public static readonly StringEqualConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && parameter is string p)
            return string.Equals(s, p, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
