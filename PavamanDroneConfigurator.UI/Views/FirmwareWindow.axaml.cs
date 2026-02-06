using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PavamanDroneConfigurator.UI.Views;

public partial class FirmwareWindow : Window
{
    /// <summary>
    /// Event raised when user clicks the Back button to return to Entry Page.
    /// </summary>
    public new event System.EventHandler? BackRequested;

    public FirmwareWindow()
    {
        InitializeComponent();
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        // Just raise the event - App.axaml.cs handles the navigation and closing
        BackRequested?.Invoke(this, System.EventArgs.Empty);
    }
}
