using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PavamanDroneConfigurator.UI.ViewModels;
using PavamanDroneConfigurator.UI.Controls;
using System.Diagnostics;

namespace PavamanDroneConfigurator.UI.Views;

/// <summary>
/// Live Map Page - Real-time drone telemetry visualization with Cesium 3D
/// Supports multiple view modes: Top-Down, 3D Chase, Free Roam
/// Uses CesiumJS for 3D mapping
/// </summary>
public partial class LiveMapPage : UserControl
{
    private CesiumMapView? _cesiumMap;
    private bool _isInitialized;
    private int _updateCount = 0;

    public LiveMapPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Debug.WriteLine("[LiveMapPage] Constructor called");
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        
        Debug.WriteLine("[LiveMapPage] OnLoaded - Finding CesiumMap control");
        _cesiumMap = this.FindControl<CesiumMapView>("CesiumMap");
        
        if (_cesiumMap == null)
        {
            Debug.WriteLine("[LiveMapPage] ERROR: CesiumMap control not found!");
            return;
        }
        
        Debug.WriteLine("[LiveMapPage] CesiumMap control found, setting up event handlers");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            Debug.WriteLine($"[LiveMapPage] ViewModel connected. IsConnected={vm.IsConnected}, HasValidPosition={vm.HasValidPosition}");
            
            vm.PositionUpdated += OnPositionUpdated;
            vm.FlightPathCleared += OnFlightPathCleared;
            vm.RecenterRequested += OnRecenterRequested;
            vm.ViewModeChanged += OnViewModeChanged;
            
            // Subscribe to map ready event
            _cesiumMap.MapReady += OnMapReady;
            _cesiumMap.MapError += OnMapError;
            
            Debug.WriteLine("[LiveMapPage] All event handlers connected");
        }
        else
        {
            Debug.WriteLine("[LiveMapPage] WARNING: DataContext is not LiveMapPageViewModel");
        }

        _isInitialized = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] OnUnloaded");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.PositionUpdated -= OnPositionUpdated;
            vm.FlightPathCleared -= OnFlightPathCleared;
            vm.RecenterRequested -= OnRecenterRequested;
            vm.ViewModeChanged -= OnViewModeChanged;
        }
        
        if (_cesiumMap != null)
        {
            _cesiumMap.MapReady -= OnMapReady;
            _cesiumMap.MapError -= OnMapError;
        }
    }

    private void OnMapReady(object? sender, EventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] ? Cesium map is ready!");
        
        // If we already have telemetry data, update the map
        if (DataContext is LiveMapPageViewModel vm)
        {
            Debug.WriteLine($"[LiveMapPage] Checking for existing telemetry data: HasValidPosition={vm.HasValidPosition}");
            
            if (vm.HasValidPosition)
            {
                Debug.WriteLine($"[LiveMapPage] Updating map with initial position: {vm.Latitude:F6}, {vm.Longitude:F6}");
                UpdateDroneOnMap(vm);
            }
            else
            {
                Debug.WriteLine("[LiveMapPage] No valid position yet, waiting for telemetry...");
            }
        }
    }
    
    private void OnMapError(object? sender, string error)
    {
        Debug.WriteLine($"[LiveMapPage] ? Map error: {error}");
    }

    private void OnPositionUpdated(object? sender, DronePositionUpdateEventArgs e)
    {
        _updateCount++;
        
        // Log every 10th update to avoid spam
        if (_updateCount % 10 == 0)
        {
            Debug.WriteLine($"[LiveMapPage] Position update #{_updateCount}: Lat={e.Latitude:F6}, Lon={e.Longitude:F6}, Alt={e.Altitude:F1}m, Hdg={e.Heading:F0}°");
        }
        
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_cesiumMap == null)
                {
                    if (_updateCount % 10 == 0)
                    {
                        Debug.WriteLine("[LiveMapPage] WARNING: CesiumMap is null, cannot update");
                    }
                    return;
                }
                
                // Update drone position on Cesium map
                _cesiumMap.UpdateDronePosition(
                    e.Latitude,
                    e.Longitude,
                    e.Altitude,
                    e.Heading,
                    e.Pitch,
                    e.Roll,
                    e.IsArmed,
                    e.GroundSpeed,
                    e.VerticalSpeed
                );
                
                if (_updateCount % 10 == 0)
                {
                    Debug.WriteLine($"[LiveMapPage] ? Map updated successfully (#{_updateCount})");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiveMapPage] ? Error updating position: {ex.Message}");
            }
        });
    }

    private void OnViewModeChanged(object? sender, ViewModeChangedEventArgs e)
    {
        if (_cesiumMap == null) return;
        
        Debug.WriteLine($"[LiveMapPage] View mode changing to: {e.ViewMode}");
        
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // Map view mode strings to CesiumMapView enum
                var viewMode = e.ViewMode switch
                {
                    "topdown" => CesiumMapView.ViewMode.TopDown,
                    "chase3d" => CesiumMapView.ViewMode.Chase3D,
                    "free" => CesiumMapView.ViewMode.FreeRoam,
                    _ => CesiumMapView.ViewMode.TopDown
                };
                
                _cesiumMap.CurrentViewMode = viewMode;
                
                Debug.WriteLine($"[LiveMapPage] ? View mode changed to: {e.ViewMode}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiveMapPage] ? Error changing view mode: {ex.Message}");
            }
        });
    }

    private void OnFlightPathCleared(object? sender, EventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] Flight path clear requested");
        
        Dispatcher.UIThread.Post(() =>
        {
            _cesiumMap?.ClearFlightPath();
            Debug.WriteLine("[LiveMapPage] ? Flight path cleared");
        });
    }

    private void OnRecenterRequested(object? sender, EventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] Recenter requested");
        
        Dispatcher.UIThread.Post(() =>
        {
            _cesiumMap?.CenterOnDrone(true);
            Debug.WriteLine("[LiveMapPage] ? Recentered on drone");
        });
    }

    private void UpdateDroneOnMap(LiveMapPageViewModel vm)
    {
        if (_cesiumMap == null || !vm.HasValidPosition)
        {
            Debug.WriteLine($"[LiveMapPage] Cannot update map: CesiumMap={_cesiumMap != null}, HasValidPosition={vm.HasValidPosition}");
            return;
        }
        
        Debug.WriteLine($"[LiveMapPage] Updating drone on map: {vm.Latitude:F6}, {vm.Longitude:F6}, {vm.Altitude:F1}m");
        
        _cesiumMap.UpdateDronePosition(
            vm.Latitude,
            vm.Longitude,
            vm.Altitude,
            vm.Heading,
            vm.Pitch,
            vm.Roll,
            vm.IsArmed,
            vm.GroundSpeed,
            vm.VerticalSpeed
        );
    }
}
