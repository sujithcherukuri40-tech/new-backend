using Avalonia.Controls;
using Avalonia.Interactivity;
using PavamanDroneConfigurator.UI.ViewModels;
using PavamanDroneConfigurator.UI.ViewModels.Admin;
using System;
using System.Linq;
using Avalonia.Controls.Notifications;
using views = PavamanDroneConfigurator.UI.Views;
using adminViews = PavamanDroneConfigurator.UI.Views.Admin;

namespace PavamanDroneConfigurator.UI.Views;

public partial class MainWindow : Window
{
    private Button? _lastActiveButton;
    private WindowNotificationManager? _notificationManager;

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
            if (this.FindControl<StackPanel>("NavigationMenu") is StackPanel navMenu)
            {
                var firstButton = navMenu.Children.OfType<Button>()
                    .FirstOrDefault(b => b.Classes.Contains("nav-button"));
                if (firstButton != null && firstButton.CommandParameter is ViewModelBase pageVm)
                {
                    var view = CreateView(pageVm);
                    if (view != null)
                    {
                        vm.SetCurrentPage(pageVm, view);
                        SetActiveButton(firstButton);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ShowNotification("Navigation Error", ex.Message, NotificationType.Error);
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
                    ShowNotification("Navigation", $"Switched to {button.Content}", NotificationType.Success);
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
            _ => null
        };
    }

    private void SetActiveButton(Button activeButton)
    {
        if (_lastActiveButton != null && _lastActiveButton.Classes.Contains("nav-button-active"))
        {
            _lastActiveButton.Classes.Remove("nav-button-active");
        }

        if (!activeButton.Classes.Contains("nav-button-active"))
        {
            activeButton.Classes.Add("nav-button-active");
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