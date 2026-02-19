using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PavamanDroneConfigurator.UI.ViewModels;

namespace PavamanDroneConfigurator.UI.Views;

public partial class TermsAndConditionsDialog : Window
{
    public TermsAndConditionsDialog()
    {
        InitializeComponent();
        
        var viewModel = new TermsAndConditionsViewModel();
        viewModel.CloseRequested += (_, _) => Close();
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
