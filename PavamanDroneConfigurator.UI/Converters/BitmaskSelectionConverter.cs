using Avalonia.Data.Converters;
using Avalonia.Data;
using System;
using System.Globalization;
using System.Linq;
using PavamanDroneConfigurator.Core.Models;
using System.Collections.ObjectModel;

namespace PavamanDroneConfigurator.UI.Converters;

public class BitmaskSelectionConverter : IValueConverter
{
    public static readonly BitmaskSelectionConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = value as ObservableCollection<ParameterOption>;
        var option = parameter as ParameterOption;
        return selected?.Any(x => x.Value == option?.Value) ?? false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
