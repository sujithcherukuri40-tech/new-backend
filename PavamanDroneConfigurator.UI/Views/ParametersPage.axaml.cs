using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.UI.ViewModels;
using System;

namespace PavamanDroneConfigurator.UI.Views;

public partial class ParametersPage : UserControl
{
    public ParametersPage()
    {
        InitializeComponent();
        Console.WriteLine($">>> LOADED PARAMETERS PAGE FROM: {GetType().FullName}");
        Console.WriteLine($">>> Assembly: {GetType().Assembly.Location}");
        Console.WriteLine($">>> Current Directory: {Environment.CurrentDirectory}");
    }

    /// <summary>
    /// Handles parameter row selection when clicked.
    /// </summary>
    private void OnParameterRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is DroneParameter parameter)
        {
            if (DataContext is ParametersPageViewModel vm)
            {
                vm.SelectedParameter = parameter;
            }
        }
    }

    private void OnBitmaskOptionChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParametersPageViewModel vm)
            return;

        if (sender is CheckBox cb &&
            cb.DataContext is ParameterOption opt &&
            vm.SelectedParameter != null)
        {
            if (cb.IsChecked == true)
            {
                if (!vm.SelectedParameter.SelectedBitmaskOptions.Contains(opt))
                    vm.SelectedParameter.SelectedBitmaskOptions.Add(opt);
            }
            else
            {
                vm.SelectedParameter.SelectedBitmaskOptions.Remove(opt);
            }

            vm.SelectedParameter.UpdateValueFromBitmask();
        }
    }
}
