using Avalonia.Controls;
using PavamanDroneConfigurator.UI.ViewModels.Auth;

namespace PavamanDroneConfigurator.UI.Views.Auth;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is LoginViewModel viewModel)
        {
            viewModel.ViewTermsRequested += OnViewTermsRequested;
        }
    }

    private async void OnViewTermsRequested(object? sender, System.EventArgs e)
    {
        var dialog = new TermsAndConditionsDialog();
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window window)
        {
            await dialog.ShowDialog(window);
        }
    }
}
