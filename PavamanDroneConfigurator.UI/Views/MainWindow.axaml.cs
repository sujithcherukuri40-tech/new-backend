using Avalonia.Controls;
using Avalonia.Interactivity;
using PavamanDroneConfigurator.UI.ViewModels;
using System;
using System.Linq;
using System.Reflection;

namespace PavamanDroneConfigurator.UI.Views;

public partial class MainWindow : Window
{
    private Button? _lastActiveButton;

    public MainWindow()
    {
        InitializeComponent();
        
        // Set initial active state when the window is loaded
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("[MainWindow] Window loaded, finding initial button...");
        
        // Find the first navigation button and set it as active
        // We use the visual tree to find buttons with nav-button class
        if (Content is Grid mainGrid)
        {
            try
            {
                var sidebar = mainGrid.Children.OfType<Border>().FirstOrDefault();
                if (sidebar?.Child is Grid sidebarGrid)
                {
                    var scrollViewer = sidebarGrid.Children.OfType<ScrollViewer>().FirstOrDefault();
                    if (scrollViewer?.Content is StackPanel navMenu)
                    {
                        var firstButton = navMenu.Children.OfType<Button>()
                            .FirstOrDefault(b => b.Classes.Contains("nav-button"));
                        if (firstButton != null)
                        {
                            Console.WriteLine($"[MainWindow] Found first nav button: {firstButton.Content}");
                            SetActiveButton(firstButton);
                        }
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
        if (sender is not Button button)
            return;

        Console.WriteLine($"[MainWindow] NavButton_Click: {button.Content}");
        
        if (DataContext is not MainWindowViewModel vm)
        {
            Console.WriteLine("[MainWindow] ERROR: DataContext is not MainWindowViewModel");
            return;
        }

        ViewModelBase? page = null;

        // First try to get page from CommandParameter binding
        if (button.CommandParameter is ViewModelBase boundPage)
        {
            page = boundPage;
            Console.WriteLine($"[MainWindow] Got page from CommandParameter: {page.GetType().Name}");
        }
        // Fallback: use Tag to find the property on the ViewModel
        else if (button.Tag is string propertyName && !string.IsNullOrEmpty(propertyName))
        {
            Console.WriteLine($"[MainWindow] CommandParameter is null, using Tag fallback: {propertyName}");
            
            // Use reflection to get the page property from MainWindowViewModel
            var property = vm.GetType().GetProperty(propertyName);
            if (property != null)
            {
                var value = property.GetValue(vm);
                if (value is ViewModelBase vmPage)
                {
                    page = vmPage;
                    Console.WriteLine($"[MainWindow] Got page via reflection: {page.GetType().Name}");
                }
                else
                {
                    Console.WriteLine($"[MainWindow] Property {propertyName} returned null or non-ViewModelBase: {value?.GetType().Name ?? "null"}");
                }
            }
            else
            {
                Console.WriteLine($"[MainWindow] Property {propertyName} not found on MainWindowViewModel");
            }
        }
        else
        {
            Console.WriteLine($"[MainWindow] No CommandParameter and no Tag - cannot navigate");
        }

        // Navigate if we got a valid page
        if (page != null)
        {
            Console.WriteLine($"[MainWindow] Navigating to: {page.GetType().Name}");
            vm.CurrentPage = page;
            SetActiveButton(button);
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
