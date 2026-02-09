using Avalonia.Data.Converters;
using Avalonia.Media;
using PavamanDroneConfigurator.UI.ViewModels;
using System;
using System.Globalization;

namespace PavamanDroneConfigurator.UI.Converters;

/// <summary>
/// Converters for flashing step progress indicators
/// </summary>
public static class FlashingStepConverters
{
    /// <summary>
    /// Converts FlashingStep to background color based on current step
    /// </summary>
    public static readonly IValueConverter StepToBackground = new StepToBackgroundConverter();
    
    /// <summary>
    /// Converts FlashingStep to opacity based on current step
    /// </summary>
    public static readonly IValueConverter StepToOpacity = new StepToOpacityConverter();
}

public class StepToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush CompletedBrush = new(Color.Parse("#22C55E")); // Green
    private static readonly SolidColorBrush ActiveBrush = new(Color.Parse("#3B82F6")); // Blue
    private static readonly SolidColorBrush PendingBrush = new(Color.Parse("#64748B")); // Gray
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FlashingStep currentStep || parameter is not string stepName)
            return PendingBrush;
        
        var targetStep = Enum.Parse<FlashingStep>(stepName);
        
        if (currentStep == FlashingStep.Complete)
            return CompletedBrush;
            
        if ((int)currentStep > (int)targetStep)
            return CompletedBrush;
        
        if (currentStep == targetStep)
            return ActiveBrush;
            
        return PendingBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StepToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FlashingStep currentStep || parameter is not string stepName)
            return 0.5;
        
        var targetStep = Enum.Parse<FlashingStep>(stepName);
        
        if (currentStep == FlashingStep.Complete)
            return 1.0;
            
        if ((int)currentStep >= (int)targetStep)
            return 1.0;
            
        return 0.5;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
