using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PavamanDroneConfigurator.UI.Views;

public partial class ParamSyncDialog : Window
{
    public ParamSyncDialog()
    {
        InitializeComponent();
    }

    private void OnSyncToDroneClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnKeepCurrentClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
