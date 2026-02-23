using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PavamanDroneConfigurator.UI.ViewModels;

namespace PavamanDroneConfigurator.UI.Views;

public partial class DisclaimerDialog : Window
{
    public DisclaimerDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DisclaimerDialogViewModel viewModel && viewModel.UserAcknowledged)
        {
            Close(true);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is DisclaimerDialogViewModel viewModel)
        {
            viewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(DisclaimerDialogViewModel.DialogResult) && 
                    viewModel.DialogResult.HasValue)
                {
                    Close(viewModel.DialogResult.Value);
                }
            };
        }
    }
}
