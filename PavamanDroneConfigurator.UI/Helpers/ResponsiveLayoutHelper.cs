using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PavamanDroneConfigurator.UI.Converters;

namespace PavamanDroneConfigurator.UI.Helpers;

/// <summary>
/// Helper class that provides responsive layout properties based on window dimensions.
/// Can be used as a DataContext or bound to individual properties.
/// Automatically updates when window size changes.
/// 
/// Usage in ViewModel:
/// public ResponsiveLayoutHelper ResponsiveHelper { get; }
/// public MyViewModel(Window window) {
///     ResponsiveHelper = new ResponsiveLayoutHelper(window);
/// }
/// 
/// Usage in XAML:
/// <TextBlock FontSize="{Binding ResponsiveHelper.FontSize}"/>
/// <Grid Visibility="{Binding ResponsiveHelper.IsSidebarVisible}"/>
/// </summary>
public class ResponsiveLayoutHelper : INotifyPropertyChanged
{
    private readonly Window? _window;
    private double _windowWidth = 1024;
    private double _windowHeight = 768;
    private FormFactor _formFactor = FormFactor.Desktop;
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    public ResponsiveLayoutHelper()
    {
        // Parameterless constructor for design-time
    }
    
    public ResponsiveLayoutHelper(Window window)
    {
        _window = window;
        
        // Initial values
        UpdateDimensions(window.Bounds.Width, window.Bounds.Height);
        
        // Subscribe to size changes
        window.PropertyChanged += OnWindowPropertyChanged;
    }
    
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.BoundsProperty && e.NewValue is Rect bounds)
        {
            UpdateDimensions(bounds.Width, bounds.Height);
        }
    }
    
    private void UpdateDimensions(double width, double height)
    {
        if (Math.Abs(_windowWidth - width) < 1 && Math.Abs(_windowHeight - height) < 1)
            return;
            
        _windowWidth = width;
        _windowHeight = height;
        _formFactor = ResponsiveBreakpoints.GetFormFactor(width);
        
        // Notify all property changes
        OnPropertyChanged(nameof(WindowWidth));
        OnPropertyChanged(nameof(WindowHeight));
        OnPropertyChanged(nameof(FormFactor));
        OnPropertyChanged(nameof(IsMobile));
        OnPropertyChanged(nameof(IsTablet));
        OnPropertyChanged(nameof(IsDesktop));
        OnPropertyChanged(nameof(IsLargeDesktop));
        OnPropertyChanged(nameof(IsCompactHeight));
        OnPropertyChanged(nameof(IsSidebarCollapsed));
        OnPropertyChanged(nameof(IsSidebarExpanded));
        OnPropertyChanged(nameof(ColumnCount));
        OnPropertyChanged(nameof(FontSizeSmall));
        OnPropertyChanged(nameof(FontSizeNormal));
        OnPropertyChanged(nameof(FontSizeLarge));
        OnPropertyChanged(nameof(FontSizeTitle));
        OnPropertyChanged(nameof(SpacingSmall));
        OnPropertyChanged(nameof(SpacingNormal));
        OnPropertyChanged(nameof(SpacingLarge));
        OnPropertyChanged(nameof(PaddingSmall));
        OnPropertyChanged(nameof(PaddingNormal));
        OnPropertyChanged(nameof(PaddingLarge));
        OnPropertyChanged(nameof(SidebarWidth));
        OnPropertyChanged(nameof(CardWidth));
        OnPropertyChanged(nameof(ContentMaxWidth));
    }
    
    #region Dimension Properties
    
    /// <summary>
    /// Current window width in pixels.
    /// </summary>
    public double WindowWidth => _windowWidth;
    
    /// <summary>
    /// Current window height in pixels.
    /// </summary>
    public double WindowHeight => _windowHeight;
    
    /// <summary>
    /// Current form factor based on window width.
    /// </summary>
    public FormFactor FormFactor => _formFactor;
    
    #endregion
    
    #region Boolean Form Factor Properties
    
    /// <summary>
    /// True if window width is mobile-sized (< 600px).
    /// </summary>
    public bool IsMobile => _formFactor == FormFactor.Mobile;
    
    /// <summary>
    /// True if window width is tablet-sized (600-1024px).
    /// </summary>
    public bool IsTablet => _formFactor == FormFactor.Tablet;
    
    /// <summary>
    /// True if window width is desktop-sized (1025-1440px).
    /// </summary>
    public bool IsDesktop => _formFactor == FormFactor.Desktop;
    
    /// <summary>
    /// True if window width is large desktop-sized (> 1440px).
    /// </summary>
    public bool IsLargeDesktop => _formFactor == FormFactor.LargeDesktop;
    
    /// <summary>
    /// True if window height is compact (< 600px).
    /// </summary>
    public bool IsCompactHeight => ResponsiveBreakpoints.IsCompactHeight(_windowHeight);
    
    /// <summary>
    /// True if window is tablet or larger (>= 600px).
    /// </summary>
    public bool IsTabletOrLarger => _formFactor >= FormFactor.Tablet;
    
    /// <summary>
    /// True if window is desktop or larger (>= 1025px).
    /// </summary>
    public bool IsDesktopOrLarger => _formFactor >= FormFactor.Desktop;
    
    #endregion
    
    #region Sidebar Properties
    
    /// <summary>
    /// True if sidebar should be collapsed (mobile/tablet).
    /// </summary>
    public bool IsSidebarCollapsed => _windowWidth < 800;
    
    /// <summary>
    /// True if sidebar should be expanded (desktop+).
    /// </summary>
    public bool IsSidebarExpanded => !IsSidebarCollapsed;
    
    /// <summary>
    /// Recommended sidebar width based on window size.
    /// </summary>
    public double SidebarWidth => IsSidebarCollapsed ? 60 : 240;
    
    #endregion
    
    #region Grid Properties
    
    /// <summary>
    /// Recommended number of columns for grid layouts.
    /// </summary>
    public int ColumnCount => _formFactor switch
    {
        FormFactor.Mobile => 1,
        FormFactor.Tablet => 2,
        FormFactor.Desktop => 3,
        FormFactor.LargeDesktop => 4,
        _ => 3
    };
    
    #endregion
    
    #region Typography Properties
    
    /// <summary>
    /// Small font size (captions, labels).
    /// </summary>
    public double FontSizeSmall => _formFactor switch
    {
        FormFactor.Mobile => 10,
        FormFactor.Tablet => 11,
        _ => 12
    };
    
    /// <summary>
    /// Normal/body font size.
    /// </summary>
    public double FontSizeNormal => _formFactor switch
    {
        FormFactor.Mobile => 12,
        FormFactor.Tablet => 13,
        _ => 14
    };
    
    /// <summary>
    /// Large font size (subtitles).
    /// </summary>
    public double FontSizeLarge => _formFactor switch
    {
        FormFactor.Mobile => 14,
        FormFactor.Tablet => 16,
        _ => 18
    };
    
    /// <summary>
    /// Title font size.
    /// </summary>
    public double FontSizeTitle => _formFactor switch
    {
        FormFactor.Mobile => 18,
        FormFactor.Tablet => 20,
        FormFactor.Desktop => 22,
        FormFactor.LargeDesktop => 24,
        _ => 22
    };
    
    #endregion
    
    #region Spacing Properties
    
    /// <summary>
    /// Small spacing (between tightly grouped elements).
    /// </summary>
    public double SpacingSmall => _formFactor switch
    {
        FormFactor.Mobile => 4,
        FormFactor.Tablet => 6,
        _ => 8
    };
    
    /// <summary>
    /// Normal spacing (between standard elements).
    /// </summary>
    public double SpacingNormal => _formFactor switch
    {
        FormFactor.Mobile => 8,
        FormFactor.Tablet => 12,
        _ => 16
    };
    
    /// <summary>
    /// Large spacing (between sections).
    /// </summary>
    public double SpacingLarge => _formFactor switch
    {
        FormFactor.Mobile => 16,
        FormFactor.Tablet => 20,
        _ => 24
    };
    
    #endregion
    
    #region Padding Properties
    
    /// <summary>
    /// Small padding for compact elements.
    /// </summary>
    public Thickness PaddingSmall => new(_formFactor switch
    {
        FormFactor.Mobile => 4,
        FormFactor.Tablet => 6,
        _ => 8
    });
    
    /// <summary>
    /// Normal padding for cards and containers.
    /// </summary>
    public Thickness PaddingNormal => new(_formFactor switch
    {
        FormFactor.Mobile => 8,
        FormFactor.Tablet => 12,
        _ => 16
    });
    
    /// <summary>
    /// Large padding for page margins.
    /// </summary>
    public Thickness PaddingLarge => new(_formFactor switch
    {
        FormFactor.Mobile => 12,
        FormFactor.Tablet => 16,
        _ => 24
    });
    
    #endregion
    
    #region Layout Properties
    
    /// <summary>
    /// Recommended card width for wrap panels.
    /// </summary>
    public double CardWidth => _formFactor switch
    {
        FormFactor.Mobile => _windowWidth - 32,
        FormFactor.Tablet => Math.Max(280, (_windowWidth - 64) / 2),
        FormFactor.Desktop => Math.Max(280, (_windowWidth - 96) / 3),
        FormFactor.LargeDesktop => Math.Max(300, (_windowWidth - 120) / 4),
        _ => 320
    };
    
    /// <summary>
    /// Maximum content width (for centered content layouts).
    /// </summary>
    public double ContentMaxWidth => _formFactor switch
    {
        FormFactor.Mobile => double.PositiveInfinity, // Full width on mobile
        FormFactor.Tablet => 720,
        FormFactor.Desktop => 960,
        FormFactor.LargeDesktop => 1200,
        _ => 960
    };
    
    #endregion
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    /// <summary>
    /// Cleanup - call when window is closing.
    /// </summary>
    public void Dispose()
    {
        if (_window != null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
        }
    }
}

/// <summary>
/// Attached properties for responsive behavior on any control.
/// Usage: <Border helpers:ResponsiveAttached.HideOnMobile="True"/>
/// </summary>
public static class ResponsiveAttached
{
    #region HideOnMobile
    
    public static readonly AttachedProperty<bool> HideOnMobileProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("HideOnMobile", typeof(ResponsiveAttached));
    
    public static bool GetHideOnMobile(Control element) => element.GetValue(HideOnMobileProperty);
    public static void SetHideOnMobile(Control element, bool value) => element.SetValue(HideOnMobileProperty, value);
    
    #endregion
    
    #region HideOnDesktop
    
    public static readonly AttachedProperty<bool> HideOnDesktopProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("HideOnDesktop", typeof(ResponsiveAttached));
    
    public static bool GetHideOnDesktop(Control element) => element.GetValue(HideOnDesktopProperty);
    public static void SetHideOnDesktop(Control element, bool value) => element.SetValue(HideOnDesktopProperty, value);
    
    #endregion
    
    #region ShowOnlyOnMobile
    
    public static readonly AttachedProperty<bool> ShowOnlyOnMobileProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ShowOnlyOnMobile", typeof(ResponsiveAttached));
    
    public static bool GetShowOnlyOnMobile(Control element) => element.GetValue(ShowOnlyOnMobileProperty);
    public static void SetShowOnlyOnMobile(Control element, bool value) => element.SetValue(ShowOnlyOnMobileProperty, value);
    
    #endregion
}
