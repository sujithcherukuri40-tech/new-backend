using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using System.Collections.Generic;

namespace PavamanDroneConfigurator.UI.ViewModels;

public partial class AirframePageViewModel : ViewModelBase
{
    private readonly IAirframeService _airframeService;
    private readonly IConnectionService _connectionService;
    private readonly IParameterService _parameterService;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _selectedFrameClass;

    [ObservableProperty]
    private int _selectedFrameType;

    [ObservableProperty]
    private string _currentFrameName = "Not loaded";

    // Available frame classes - Complete ArduPilot FRAME_CLASS list
    public List<FrameClassOption> AvailableFrameClasses { get; } = new()
    {
        new FrameClassOption(0, "Undefined", "Not configured"),
        new FrameClassOption(1, "Quad", "Standard quadcopter with 4 motors"),
        new FrameClassOption(2, "Hexa", "Hexacopter with 6 motors"),
        new FrameClassOption(3, "Octa", "Octocopter with 8 motors"),
        new FrameClassOption(4, "OctaQuad", "Octo-Quad configuration"),
        new FrameClassOption(5, "Y6", "Y6 configuration"),
        new FrameClassOption(6, "Heli", "Traditional helicopter"),
        new FrameClassOption(7, "Tri", "Tricopter with 3 motors"),
        new FrameClassOption(8, "SingleCopter", "Single motor copter"),
        new FrameClassOption(9, "CoaxCopter", "Coaxial copter"),
        new FrameClassOption(10, "BiCopter", "Dual rotor copter"),
        new FrameClassOption(11, "Heli Dual", "Dual rotor helicopter"),
        new FrameClassOption(12, "DodecaHexa", "12-motor hexacopter"),
        new FrameClassOption(13, "HeliQuad", "Helicopter-Quad hybrid"),
        new FrameClassOption(14, "Deca", "Decacopter with 10 motors")
    };

    // Available frame types (configurations) - Complete ArduPilot FRAME_TYPE list
    public List<FrameTypeOption> AvailableFrameTypes { get; } = new()
    {
        new FrameTypeOption(0, "Plus (+)", "Plus configuration"),
        new FrameTypeOption(1, "X", "X configuration"),
        new FrameTypeOption(2, "V", "V configuration"),
        new FrameTypeOption(3, "H", "H configuration"),
        new FrameTypeOption(4, "V-Tail", "V-Tail configuration"),
        new FrameTypeOption(5, "A-Tail", "A-Tail configuration"),
        new FrameTypeOption(6, "Y6B (Reversed)", "Y6B reversed motor order"),
        new FrameTypeOption(7, "Y6F2", "Y6 forward variant 2"),
        new FrameTypeOption(8, "BetaFlight X", "BetaFlight X motor order"),
        new FrameTypeOption(9, "DJI X", "DJI X motor order"),
        new FrameTypeOption(10, "Y6B", "Y6B motor order"),
        new FrameTypeOption(11, "Y6F", "Y6 forward motor order"),
        new FrameTypeOption(12, "ClockWise X", "Clockwise X motor order"),
        new FrameTypeOption(13, "NanoQuad", "Nano quadcopter layout"),
        new FrameTypeOption(14, "Deca CW X", "Deca clockwise X layout")
    };

    public AirframePageViewModel(IAirframeService airframeService, IConnectionService connectionService, IParameterService parameterService)
    {
        _airframeService = airframeService;
        _connectionService = connectionService;
        _parameterService = parameterService;

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _parameterService.ParameterDownloadCompleted += OnParameterDownloadCompleted;
        
        IsConnected = _connectionService.IsConnected;
        
        // If already connected and parameters downloaded, load settings
        if (_connectionService.IsConnected && _parameterService.IsParameterDownloadComplete)
        {
            _ = LoadSettingsAsync();
        }
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            
            if (!connected)
            {
                StatusMessage = "Disconnected";
                CurrentFrameName = "Not connected";
            }
            else
            {
                StatusMessage = "Connected - Waiting for parameters...";
            }
        });
    }

    private void OnParameterDownloadCompleted(object? sender, bool success)
    {
        if (success)
        {
            // Auto-load airframe settings after parameters are downloaded
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await LoadSettingsAsync();
            });
        }
    }

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected. Please connect first.";
            return;
        }

        if (!_parameterService.IsParameterDownloadComplete)
        {
            StatusMessage = "Waiting for parameters to download...";
            return;
        }

        IsLoading = true;
        StatusMessage = "Loading airframe settings from drone...";

        try
        {
            var settings = await _airframeService.GetAirframeSettingsAsync();
            if (settings != null)
            {
                SelectedFrameClass = settings.FrameClass;
                SelectedFrameType = settings.FrameType;
                CurrentFrameName = settings.FrameName;

                StatusMessage = $"Airframe: {settings.FrameName}";
            }
            else
            {
                StatusMessage = "Could not determine airframe settings";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected. Cannot apply settings.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Applying airframe settings...";

        try
        {
            var frameClassName = AvailableFrameClasses.FirstOrDefault(f => f.Value == SelectedFrameClass)?.Name ?? "Unknown";
            var frameTypeName = AvailableFrameTypes.FirstOrDefault(f => f.Value == SelectedFrameType)?.Name ?? "Unknown";
            
            var settings = new AirframeSettings
            {
                FrameClass = SelectedFrameClass,
                FrameType = SelectedFrameType,
                FrameName = $"{frameClassName} {frameTypeName}"
            };

            var success = await _airframeService.UpdateAirframeSettingsAsync(settings);
            
            if (success)
            {
                CurrentFrameName = settings.FrameName;
                StatusMessage = "Airframe settings applied successfully. Reboot required for changes to take effect.";
            }
            else
            {
                StatusMessage = "Failed to apply airframe settings";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error applying settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshSettingsAsync()
    {
        await LoadSettingsAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _parameterService.ParameterDownloadCompleted -= OnParameterDownloadCompleted;
        }
        base.Dispose(disposing);
    }
}

// Helper classes for dropdown options
public class FrameClassOption
{
    public int Value { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public FrameClassOption(int value, string name, string description)
    {
        Value = value;
        Name = name;
        Description = description;
    }
}

public class FrameTypeOption
{
    public int Value { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public FrameTypeOption(int value, string name, string description)
    {
        Value = value;
        Name = name;
        Description = description;
    }
}
