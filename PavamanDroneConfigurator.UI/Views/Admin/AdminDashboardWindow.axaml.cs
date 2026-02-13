using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PavamanDroneConfigurator.UI.Views.Admin;

public partial class AdminDashboardWindow : Window
{
    /// <summary>
    /// Event raised when user clicks the Back button to return to Entry Page.
    /// </summary>
    public event System.EventHandler? BackRequested;

    public AdminDashboardWindow()
    {
        InitializeComponent();
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, System.EventArgs.Empty);
    }
}
