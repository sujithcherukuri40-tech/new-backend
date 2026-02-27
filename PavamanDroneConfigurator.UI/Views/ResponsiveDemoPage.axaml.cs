using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PavamanDroneConfigurator.UI.Views;

public partial class ResponsiveDemoPage : UserControl
{
    public ResponsiveDemoPage()
    {
        InitializeComponent();
        DataContext = new ResponsiveDemoViewModel();
    }
    
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        // Get the top-level window and subscribe to size changes
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.PropertyChanged += OnWindowPropertyChanged;
            UpdateSize(window.Bounds.Width, window.Bounds.Height);
        }
    }
    
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
        }
    }
    
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.BoundsProperty && sender is Window window)
        {
            UpdateSize(window.Bounds.Width, window.Bounds.Height);
        }
    }
    
    private void UpdateSize(double width, double height)
    {
        if (DataContext is ResponsiveDemoViewModel vm)
        {
            vm.WindowWidth = width;
            vm.WindowHeight = height;
        }
    }
}

/// <summary>
/// ViewModel for the Responsive Demo Page that tracks window dimensions
/// and provides computed responsive properties for binding.
/// </summary>
public partial class ResponsiveDemoViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMobile))]
    [NotifyPropertyChangedFor(nameof(IsTablet))]
    [NotifyPropertyChangedFor(nameof(IsDesktop))]
    [NotifyPropertyChangedFor(nameof(IsSidebarExpanded))]
    [NotifyPropertyChangedFor(nameof(IsSidebarCollapsed))]
    [NotifyPropertyChangedFor(nameof(IsDesktopOnly))]
    [NotifyPropertyChangedFor(nameof(IsMobileOnly))]
    [NotifyPropertyChangedFor(nameof(FormStackOrientation))]
    [NotifyPropertyChangedFor(nameof(ContentPadding))]
    [NotifyPropertyChangedFor(nameof(BreakpointName))]
    [NotifyPropertyChangedFor(nameof(WindowSizeDisplay))]
    private double _windowWidth = 1024;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowSizeDisplay))]
    private double _windowHeight = 768;
    
    // Breakpoint thresholds
    private const double MobileMaxWidth = 600;
    private const double TabletMaxWidth = 1024;
    private const double SidebarCollapseWidth = 800;
    private const double DesktopOnlyWidth = 1024;
    private const double MobileOnlyWidth = 600;
    
    /// <summary>
    /// True if window width is mobile-sized (less than 600px)
    /// </summary>
    public bool IsMobile => WindowWidth < MobileMaxWidth;
    
    /// <summary>
    /// True if window width is tablet-sized (600-1024px)
    /// </summary>
    public bool IsTablet => WindowWidth >= MobileMaxWidth && WindowWidth < TabletMaxWidth;
    
    /// <summary>
    /// True if window width is desktop-sized (1024px+)
    /// </summary>
    public bool IsDesktop => WindowWidth >= TabletMaxWidth;
    
    /// <summary>
    /// True if sidebar should be expanded (width >= 800px)
    /// </summary>
    public bool IsSidebarExpanded => WindowWidth >= SidebarCollapseWidth;
    
    /// <summary>
    /// True if sidebar should be collapsed (width less than 800px)
    /// </summary>
    public bool IsSidebarCollapsed => WindowWidth < SidebarCollapseWidth;
    
    /// <summary>
    /// True if desktop-only content should be visible (width >= 1024px)
    /// </summary>
    public bool IsDesktopOnly => WindowWidth >= DesktopOnlyWidth;
    
    /// <summary>
    /// True if mobile-only content should be visible (width less than 600px)
    /// </summary>
    public bool IsMobileOnly => WindowWidth < MobileOnlyWidth;
    
    /// <summary>
    /// Orientation for form layout - Horizontal on desktop, Vertical on mobile
    /// </summary>
    public Avalonia.Layout.Orientation FormStackOrientation => 
        WindowWidth >= MobileMaxWidth ? Avalonia.Layout.Orientation.Horizontal : Avalonia.Layout.Orientation.Vertical;
    
    /// <summary>
    /// Content padding based on screen size
    /// </summary>
    public Thickness ContentPadding
    {
        get
        {
            if (WindowWidth < MobileMaxWidth)
                return new Thickness(12);
            if (WindowWidth < TabletMaxWidth)
                return new Thickness(16);
            if (WindowWidth < 1440)
                return new Thickness(20);
            return new Thickness(24);
        }
    }
    
    /// <summary>
    /// Current breakpoint name for display
    /// </summary>
    public string BreakpointName
    {
        get
        {
            if (WindowWidth < MobileMaxWidth)
                return "Mobile (< 600px)";
            if (WindowWidth < TabletMaxWidth)
                return "Tablet (600-1024px)";
            if (WindowWidth < 1440)
                return "Desktop (1024-1440px)";
            return "Large Desktop (> 1440px)";
        }
    }
    
    /// <summary>
    /// Formatted window size display
    /// </summary>
    public string WindowSizeDisplay => $"{WindowWidth:F0}px × {WindowHeight:F0}px";
}
