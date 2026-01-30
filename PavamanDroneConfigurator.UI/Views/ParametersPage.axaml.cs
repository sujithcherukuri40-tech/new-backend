using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.UI.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace PavamanDroneConfigurator.UI.Views;

public partial class ParametersPage : UserControl
{
    private ParametersPageViewModel? ViewModel => DataContext as ParametersPageViewModel;

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
            if (ViewModel != null)
            {
                ViewModel.SelectedParameter = parameter;
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
            // Initialize collection if null
            if (parameter.SelectedBitmaskOptions == null)
            {
                parameter.SelectedBitmaskOptions = new ObservableCollection<ParameterOption>();
            }
            
            if (!parameter.SelectedBitmaskOptions.Any(o => o.Value == opt.Value))
            {
                parameter.SelectedBitmaskOptions.Add(opt);
            }
        }
        else
        {
            if (parameter.SelectedBitmaskOptions != null)
            {
                var toRemove = parameter.SelectedBitmaskOptions.FirstOrDefault(o => o.Value == opt.Value);
                if (toRemove != null)
                {
                    parameter.SelectedBitmaskOptions.Remove(toRemove);
                }
            }
        }

        // Update the parameter value from bitmask
        parameter.UpdateValueFromBitmask();
    }

    /// <summary>
    /// Handles OPTIONS column input when focus is lost.
    /// Validates and applies the input to the parameter value.
    /// </summary>
    private void OptionsInput_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is DroneParameter param && ViewModel != null)
        {
            ViewModel.ApplyOptionsInput(param);
        }
    }

    /// <summary>
    /// Handles OPTIONS column input when Enter key is pressed.
    /// Validates and applies the input to the parameter value.
    /// </summary>
    private void OptionsInput_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (sender is TextBox tb && tb.DataContext is DroneParameter param && ViewModel != null)
            {
                ViewModel.ApplyOptionsInput(param);
            }
        }
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
