using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.UI.ViewModels;
using System;
using System.Linq;

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
        if (sender is not CheckBox cb || cb.Tag is not ParameterOption opt)
            return;

        // Find the parameter this checkbox belongs to by traversing up the visual tree
        var border = FindParent<Border>(cb);
        if (border?.Tag is not DroneParameter parameter)
            return;

        // Update the bitmask selection
        if (cb.IsChecked == true)
        {
            if (!parameter.SelectedBitmaskOptions.Any(o => o.Value == opt.Value))
            {
                parameter.SelectedBitmaskOptions.Add(opt);
            }
        }
        else
        {
            var toRemove = parameter.SelectedBitmaskOptions.FirstOrDefault(o => o.Value == opt.Value);
            if (toRemove != null)
            {
                parameter.SelectedBitmaskOptions.Remove(toRemove);
            }
        }

        // Update the parameter value from bitmask
        parameter.UpdateValueFromBitmask();
    }

    private static T? FindParent<T>(Control control) where T : Control
    {
        var parent = control.Parent;
        while (parent != null)
        {
            if (parent is T typedParent)
                return typedParent;
            parent = parent.Parent;
        }
        return null;
    }
}
