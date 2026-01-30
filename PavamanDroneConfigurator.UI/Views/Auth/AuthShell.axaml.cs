using System.Threading.Tasks;
using Avalonia.Controls;
using PavamanDroneConfigurator.UI.ViewModels.Auth;

namespace PavamanDroneConfigurator.UI.Views.Auth;

public partial class AuthShell : Window
{
    public AuthShell()
    {
        InitializeComponent();
        
        Closing += OnWindowClosing;
    }

    public async Task InitializeAsync()
    {
        if (DataContext is AuthShellViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is AuthShellViewModel viewModel)
        {
            viewModel.OnWindowClosing();
        }
    }
}
