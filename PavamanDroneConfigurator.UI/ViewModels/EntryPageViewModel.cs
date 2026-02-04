using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Entry Page - the gateway after authentication.
/// Provides two paths: Firmware flashing (no connection required) or Connect to Drone.
/// </summary>
public partial class EntryPageViewModel : ViewModelBase
{
    /// <summary>
    /// Event raised when user wants to flash firmware (opens standalone firmware window).
    /// </summary>
    public event EventHandler? FirmwareRequested;

    /// <summary>
    /// Event raised when user wants to connect to a drone (navigates to ConnectionShell).
    /// </summary>
    public event EventHandler? ConnectRequested;

    /// <summary>
    /// Event raised when user wants to exit the application.
    /// </summary>
    public event EventHandler? ExitRequested;

    [ObservableProperty]
    private string _welcomeMessage = "Welcome to Pavaman Drone Configurator";

    [ObservableProperty]
    private string _subtitleMessage = "Choose an option to get started";

    /// <summary>
    /// Opens the firmware flashing window (no MAVLink connection required).
    /// </summary>
    [RelayCommand]
    private void FlashFirmware()
    {
        FirmwareRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Navigates to the connection setup flow.
    /// </summary>
    [RelayCommand]
    private void ConnectToDrone()
    {
        ConnectRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Exits the application.
    /// </summary>
    [RelayCommand]
    private void Exit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
