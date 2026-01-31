using Avalonia.Controls;
using PavamanDroneConfigurator.UI.ViewModels.Admin;

namespace PavamanDroneConfigurator.UI.Views.Admin;

public partial class AdminDashboardView : UserControl
{
    public AdminDashboardView()
    {
        InitializeComponent();
        this.AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private async void OnAttachedToVisualTree(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        this.AttachedToVisualTree -= OnAttachedToVisualTree;

        if (DataContext is AdminDashboardViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
