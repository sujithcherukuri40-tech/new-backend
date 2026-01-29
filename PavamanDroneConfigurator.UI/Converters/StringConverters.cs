using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Collection of string converters for XAML bindings.
/// </summary>
public static class StringConverters
{
    /// <summary>
    /// Converter that returns true if string is not null or empty.
    /// </summary>
    public static readonly IValueConverter IsNotNullOrEmpty = new FuncValueConverter<string?, bool>(
        value => !string.IsNullOrEmpty(value)
    );

    /// <summary>
    /// Converter that returns true if string is null or empty.
    /// </summary>
    public static readonly IValueConverter IsNullOrEmpty = new FuncValueConverter<string?, bool>(
        value => string.IsNullOrEmpty(value)
    );

    /// <summary>
    /// Converts string to uppercase.
    /// </summary>
    public static readonly IValueConverter ToUpper = new FuncValueConverter<string?, string?>(
        value => value?.ToUpperInvariant()
    );

    /// <summary>
    /// Converts string to lowercase.
    /// </summary>
    public static readonly IValueConverter ToLower = new FuncValueConverter<string?, string?>(
        value => value?.ToLowerInvariant()
    );
}
