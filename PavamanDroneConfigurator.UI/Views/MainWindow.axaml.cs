using Avalonia.Controls;
using Avalonia.Interactivity;
using PavamanDroneConfigurator.UI.ViewModels;
using PavamanDroneConfigurator.UI.ViewModels.Admin;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using views = PavamanDroneConfigurator.UI.Views;
using adminViews = PavamanDroneConfigurator.UI.Views.Admin;

namespace PavamanDroneConfigurator.UI.Views;

public partial class MainWindow : Window
{
    private Button? _lastActiveButton;
    private WindowNotificationManager? _notificationManager;

    // CSS class names for navigation styling
    private const string NavItemClass = "sidebar-nav-item";
    private const string NavActiveClass = "sidebar-nav-active";

    public MainWindow()
    {
        InitializeComponent();

        _notificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3
        };

        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        try
        {
            // Set up navigation callback for AdminDashboard
            if (vm.AdminDashboardPage != null)
            {
                vm.AdminDashboardPage.NavigateToPage = NavigateToAdminPage;
            }
            
            if (this.FindControl<StackPanel>("NavigationMenu") is StackPanel navMenu)
            {
                var firstButton = navMenu.Children.OfType<Button>()
                    .FirstOrDefault(b => b.Classes.Contains(NavItemClass));
                if (firstButton != null && firstButton.CommandParameter is ViewModelBase pageVm)
                {
                    var view = CreateView(pageVm);
                    if (view != null)
                    {
                        vm.SetCurrentPage(pageVm, view);
                        SetActiveButton(firstButton);
                        
                        // Initialize pages that need lazy loading
                        _ = InitializePageIfNeededAsync(pageVm);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ShowNotification("Navigation Error", ex.Message, NotificationType.Error);
        }
    }
    
    private void NavigateToAdminPage(string pageNameWithFilter)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        
        // Parse page name and optional filter (e.g., "AdminDashboardPage:pending")
        var parts = pageNameWithFilter.Split(':');
        var pageName = parts[0];
        var filter = parts.Length > 1 ? parts[1] : null;
        
        ViewModelBase? targetPage = pageName switch
        {
            "ParamLogsPage" => vm.ParamLogsPage,
            "FirmwareManagementPage" => vm.FirmwareManagementPage,
            "AdminPanelPage" or "AdminDashboardPage" => vm.AdminDashboardPage,
            _ => null
        };
        
        if (targetPage == null) return;
        
        // Apply filter preset if specified for AdminDashboard
        if (targetPage is AdminDashboardViewModel adminDash && filter != null)
        {
            adminDash.SetFilterPreset(filter);
        }
        
        var view = CreateView(targetPage);
        if (view != null)
        {
            vm.SetCurrentPage(targetPage, view);
            
            // Find and set the active button in the navigation menu
            if (this.FindControl<StackPanel>("NavigationMenu") is StackPanel navMenu)
            {
                // Map to actual button tag
                var buttonTag = pageName switch
                {
                    "AdminPanelPage" or "AdminDashboardPage" => "AdminDashboardPage",
                    _ => pageName
                };
                
                var button = navMenu.Children.OfType<Button>()
                    .FirstOrDefault(b => b.Tag?.ToString() == buttonTag);
                if (button != null)
                {
                    SetActiveButton(button);
                }
            }
            
            _ = InitializePageIfNeededAsync(targetPage);
        }
    }

    private void NavButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button button) return;
            if (DataContext is not MainWindowViewModel vm) return;

            ViewModelBase? page = button.CommandParameter as ViewModelBase;

            if (page == null && button.Tag is string propertyName && !string.IsNullOrEmpty(propertyName))
            {
                var prop = vm.GetType().GetProperty(propertyName);
                if (prop?.GetValue(vm) is ViewModelBase reflectedVm)
                {
                    page = reflectedVm;
                }
            }

            if (page != null)
            {
                var view = CreateView(page);
                if (view != null)
                {
                    vm.SetCurrentPage(page, view);
                    SetActiveButton(button);
                    
                    // Initialize pages that need lazy loading when navigated to
                    _ = InitializePageIfNeededAsync(page);
                }
                else
                {
                    ShowNotification("Navigation Failed", $"No view for {page.GetType().Name}", NotificationType.Error);
                }
            }
            else
            {
                ShowNotification("Navigation Failed", "No target page", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowNotification("Navigation Error", ex.Message, NotificationType.Error);
        }
    }

    /// <summary>
    /// Initializes pages that have lazy-loading requirements.
    /// This is called after navigation to defer heavy initialization until the page is actually viewed.
    /// </summary>
    private static async Task InitializePageIfNeededAsync(ViewModelBase page)
    {
        try
        {
            switch (page)
            {
                case AdvancedSettingsPageViewModel advSettings:
                    await advSettings.InitializeAsync();
                    break;
                case AdminDashboardViewModel adminDash:
                    await adminDash.InitializeAsync();
                    break;
                case FirmwareManagementViewModel fwMgmt:
                    await fwMgmt.InitializeAsync();
                    break;
                case ParamLogsViewModel paramLogs:
                    await paramLogs.InitializeAsync();
                    break;
                // Add other pages that need lazy initialization here
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Page initialization failed: {ex.Message}");
        }
    }

    private Control? CreateView(ViewModelBase vm)
    {
        return vm switch
        {
            ConnectionPageViewModel => new views.ConnectionPage { DataContext = vm },
            DroneDetailsPageViewModel => new views.DroneDetailsPage { DataContext = vm },
            AirframePageViewModel => new views.AirframePage { DataContext = vm },
            ParametersPageViewModel => new views.ParametersPage { DataContext = vm },
            SafetyPageViewModel => new views.SafetyPage { DataContext = vm },
            ProfilePageViewModel => new views.ProfilePage { DataContext = vm },
            FlightModePageViewModel => new views.FlightModesPage { DataContext = vm },
            PowerPageViewModel => new views.PowerPage { DataContext = vm },
            MotorEscPageViewModel => new views.MotorEscPage { DataContext = vm },
            PidTuningPageViewModel => new views.PidTuningPage { DataContext = vm },
            SerialConfigPageViewModel => new views.SerialConfigPage { DataContext = vm },
            RcCalibrationPageViewModel => new views.RcCalibrationPage { DataContext = vm },
            SensorsCalibrationPageViewModel => new views.SensorsCalibrationPage { DataContext = vm },
            LogAnalyzerPageViewModel => new views.LogAnalyzerPage { DataContext = vm },
            ResetParametersPageViewModel => new views.ResetParametersPage { DataContext = vm },
            SprayingConfigPageViewModel => new views.SprayingConfigPage { DataContext = vm },
            CameraConfigPageViewModel => new views.CameraConfigPage { DataContext = vm },
            AdvancedSettingsPageViewModel => new views.AdvancedSettingsPage { DataContext = vm },
            FirmwarePageViewModel => new views.FirmwarePage { DataContext = vm },
            AdminDashboardViewModel => new adminViews.AdminDashboardView { DataContext = vm },
            AdminPanelViewModel => new adminViews.AdminPanelView { DataContext = vm },
            FirmwareManagementViewModel => new adminViews.FirmwareManagementPage { DataContext = vm },
            ParamLogsViewModel => new adminViews.ParamLogsPage { DataContext = vm },
            _ => null
        };
    }

    private void SetActiveButton(Button activeButton)
    {
        // Remove active class from previous button
        if (_lastActiveButton != null && _lastActiveButton.Classes.Contains(NavActiveClass))
        {
            _lastActiveButton.Classes.Remove(NavActiveClass);
        }

        // Add active class to new button
        if (!activeButton.Classes.Contains(NavActiveClass))
        {
            activeButton.Classes.Add(NavActiveClass);
        }

        _lastActiveButton = activeButton;
    }

    private void ShowNotification(string title, string message, NotificationType type)
    {
        try
        {
            _notificationManager?.Show(new Notification(title, message, type));
        }
        catch { }
    }
}