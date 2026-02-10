using Avalonia.Controls;
using PavamanDroneConfigurator.UI.ViewModels.Admin;

namespace PavamanDroneConfigurator.UI.Views.Admin;

public partial class ParamLogsPage : UserControl
{
    public ParamLogsPage()
    {
        InitializeComponent();
    }
    
    protected override async void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (DataContext is ParamLogsViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
