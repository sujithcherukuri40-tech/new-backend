using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PavamanDroneConfigurator.UI.Views;

public partial class ParameterUpdateDialog : Window
{
    public ParameterUpdateDialog()
    {
        InitializeComponent();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
