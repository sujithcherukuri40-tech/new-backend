using Avalonia.Controls;
using Avalonia.Interactivity;
using PavamanDroneConfigurator.UI.ViewModels;
using System;
using System.Linq;

namespace PavamanDroneConfigurator.UI.Views;

public partial class MainWindow : Window
{
    private Button? _lastActiveButton;

    public MainWindow()
    {
        InitializeComponent();
        
        // Set initial active state
        Loaded += (s, e) =>
        {
            try
            {
                // Find and activate the Connection button by default
                if (this.FindControl<StackPanel>("NavigationMenu") is StackPanel navMenu)
                {
                    var firstButton = navMenu.Children.OfType<Button>().FirstOrDefault();
                    if (firstButton != null)
                    {
                        SetActiveButton(firstButton);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash app
                System.Diagnostics.Debug.WriteLine($"Error setting initial navigation: {ex.Message}");
            }
        };
    }

    private void NavButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is ViewModelBase page)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.CurrentPage = page;
                SetActiveButton(button);
            }
        }
    }

    private void SetActiveButton(Button activeButton)
    {
        // Remove active class from previous button
        if (_lastActiveButton != null && _lastActiveButton.Classes.Contains("nav-button-active"))
        {
            _lastActiveButton.Classes.Remove("nav-button-active");
        }

        // Add active class to new button
        if (!activeButton.Classes.Contains("nav-button-active"))
        {
            activeButton.Classes.Add("nav-button-active");
        }

        _lastActiveButton = activeButton;
    }
}
