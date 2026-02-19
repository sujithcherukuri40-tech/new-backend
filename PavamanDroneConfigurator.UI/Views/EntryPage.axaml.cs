using Avalonia.Controls;
using PavamanDroneConfigurator.UI.ViewModels;

namespace PavamanDroneConfigurator.UI.Views;

public partial class EntryPage : Window
{
    public EntryPage()
    {
        InitializeComponent();
        
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is EntryPageViewModel viewModel)
        {
            viewModel.ViewTermsRequested += OnViewTermsRequested;
        }
    }

    private async void OnViewTermsRequested(object? sender, System.EventArgs e)
    {
        var dialog = new TermsAndConditionsDialog();
        await dialog.ShowDialog(this);
    }
}
