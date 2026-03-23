using System;
using System.Collections.Generic;
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
/// Uses CesiumJS for 3D mapping with mission planning and spray overlay
/// </summary>
public partial class LiveMapPage : UserControl
{
    private CesiumMapView? _cesiumMap;
    private bool _isInitialized;
    private bool _isMapReady;
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

        // Subscribe to map events
        _cesiumMap.MapReady += OnMapReady;
        _cesiumMap.MapError += OnMapError;
        _cesiumMap.WaypointPlaced += OnWaypointPlaced;
        _cesiumMap.WaypointMoved += OnWaypointMoved;
        _cesiumMap.WaypointDeleted += OnWaypointDeleted;
        _cesiumMap.HomePlaced += OnHomePlaced;
        _cesiumMap.LandPlaced += OnLandPlaced;
        _cesiumMap.OrbitPlaced += OnOrbitPlaced;
        _cesiumMap.SurveyBoundaryCompleted += OnSurveyBoundaryCompleted;

        if (DataContext is LiveMapPageViewModel vm)
        {
            Debug.WriteLine($"[LiveMapPage] ViewModel connected. IsConnected={vm.IsConnected}, HasValidPosition={vm.HasValidPosition}");

            // Subscribe to ViewModel events
            vm.PositionUpdated += OnPositionUpdated;
            vm.FlightPathCleared += OnFlightPathCleared;
            vm.RecenterRequested += OnRecenterRequested;
            vm.ViewModeChanged += OnViewModeChanged;
            vm.MapTypeChanged += OnMapTypeChanged;
            vm.FollowChanged += OnFollowChanged;
            vm.MissionToolChanged += OnMissionToolChanged;

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
            vm.MapTypeChanged -= OnMapTypeChanged;
            vm.FollowChanged -= OnFollowChanged;
            vm.MissionToolChanged -= OnMissionToolChanged;
        }

        if (_cesiumMap != null)
        {
            _cesiumMap.MapReady -= OnMapReady;
            _cesiumMap.MapError -= OnMapError;
            _cesiumMap.WaypointPlaced -= OnWaypointPlaced;
            _cesiumMap.WaypointMoved -= OnWaypointMoved;
            _cesiumMap.WaypointDeleted -= OnWaypointDeleted;
            _cesiumMap.HomePlaced -= OnHomePlaced;
            _cesiumMap.LandPlaced -= OnLandPlaced;
            _cesiumMap.OrbitPlaced -= OnOrbitPlaced;
            _cesiumMap.SurveyBoundaryCompleted -= OnSurveyBoundaryCompleted;
        }
    }

    private void OnMapReady(object? sender, EventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] ? Cesium map is ready!");
        _isMapReady = true;

        if (DataContext is LiveMapPageViewModel vm)
        {
            // Initialize map settings from ViewModel
            _cesiumMap?.SetMapType(vm.GetMapTypeName());
            _cesiumMap?.SetMissionTool(ParseMissionTool(vm.ActiveMissionTool));
            
            if (_cesiumMap != null)
            {
                _cesiumMap.IsFollowing = vm.FollowDrone;
            }

            Debug.WriteLine($"[LiveMapPage] Checking for existing telemetry data: HasValidPosition={vm.HasValidPosition}");

            // If we already have valid position data, update the map immediately
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
        _isMapReady = false;
    }

    private void OnPositionUpdated(object? sender, DronePositionUpdateEventArgs e)
    {
        if (!_isMapReady || _cesiumMap == null)
        {
            return;
        }

        _updateCount++;

        // Log every 20th update to avoid spam
        if (_updateCount % 20 == 0)
        {
            Debug.WriteLine($"[LiveMapPage] Position update #{_updateCount}: Lat={e.Latitude:F6}, Lon={e.Longitude:F6}, Alt={e.Altitude:F1}m, Hdg={e.Heading:F0}°");
        }

        // Use Post for non-blocking UI update
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _cesiumMap?.UpdateDronePosition(
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiveMapPage] ? Error updating position: {ex.Message}");
            }
        });
    }

    private void OnViewModeChanged(object? sender, ViewModeChangedEventArgs e)
    {
        if (!_isMapReady || _cesiumMap == null) return;

        Debug.WriteLine($"[LiveMapPage] View mode changing to: {e.ViewMode}");

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var viewMode = e.ViewMode switch
                {
                    "topdown" => CesiumMapView.ViewMode.TopDown,
                    "chase3d" => CesiumMapView.ViewMode.Chase3D,
                    "fpv" => CesiumMapView.ViewMode.FirstPerson,
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

    private void OnMapTypeChanged(object? sender, string mapType)
    {
        if (!_isMapReady || _cesiumMap == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _cesiumMap.SetMapType(mapType);
                Debug.WriteLine($"[LiveMapPage] Map type set to: {mapType}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiveMapPage] Error setting map type: {ex.Message}");
            }
        });
    }

    private void OnFollowChanged(object? sender, bool follow)
    {
        if (!_isMapReady || _cesiumMap == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _cesiumMap.IsFollowing = follow;
            Debug.WriteLine($"[LiveMapPage] Follow mode set to: {follow}");
        });
    }

    private void OnMissionToolChanged(object? sender, string tool)
    {
        if (!_isMapReady || _cesiumMap == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _cesiumMap.SetMissionTool(ParseMissionTool(tool));
            Debug.WriteLine($"[LiveMapPage] Mission tool set to: {tool}");
        });
    }

    private void OnFlightPathCleared(object? sender, EventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] Flight path clear requested");

        Dispatcher.UIThread.Post(() =>
        {
            _cesiumMap?.ClearFlightPath();
            _cesiumMap?.ClearWaypoints();
            _cesiumMap?.ClearSprayOverlay();
            Debug.WriteLine("[LiveMapPage] ? Flight path and waypoints cleared");
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

    #region Mission Planning Events

    private void OnWaypointPlaced(object? sender, (double Latitude, double Longitude, int Index) e)
    {
        Debug.WriteLine($"[LiveMapPage] Waypoint placed: #{e.Index} at {e.Latitude:F6}, {e.Longitude:F6}");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.RegisterMissionItem("WAYPOINT");
        }
    }

    private void OnWaypointMoved(object? sender, (int Index, double Latitude, double Longitude) e)
    {
        Debug.WriteLine($"[LiveMapPage] Waypoint moved: #{e.Index} to {e.Latitude:F6}, {e.Longitude:F6}");
        // Could update mission service here if needed
    }

    private void OnWaypointDeleted(object? sender, int index)
    {
        Debug.WriteLine($"[LiveMapPage] Waypoint deleted: #{index}");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            // Decrement mission item count if needed
            if (vm.MissionItemCount > 0)
            {
                vm.MissionItemCount--;
                vm.MissionProgressText = $"Mission: {vm.MissionItemCount}/{vm.MissionItemCount} items";
            }
        }
    }

    private void OnHomePlaced(object? sender, (double Latitude, double Longitude) e)
    {
        Debug.WriteLine($"[LiveMapPage] Home placed at {e.Latitude:F6}, {e.Longitude:F6}");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.RegisterMissionItem("HOME");
        }
    }

    private void OnLandPlaced(object? sender, (double Latitude, double Longitude) e)
    {
        Debug.WriteLine($"[LiveMapPage] Land position placed at {e.Latitude:F6}, {e.Longitude:F6}");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.RegisterMissionItem("LAND");
        }
    }

    private void OnOrbitPlaced(object? sender, (double Latitude, double Longitude) e)
    {
        Debug.WriteLine($"[LiveMapPage] Orbit center placed at {e.Latitude:F6}, {e.Longitude:F6}");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.RegisterMissionItem("ORBIT");
        }
    }

    private void OnSurveyBoundaryCompleted(object? sender, List<(double Latitude, double Longitude)> boundary)
    {
        Debug.WriteLine($"[LiveMapPage] Survey boundary completed with {boundary.Count} vertices");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            // Register survey as a mission item
            vm.RegisterMissionItem("SURVEY");
            
            // TODO: Generate survey grid and render on map
            // This would integrate with IMissionService.GenerateSurveyGrid()
        }
    }

    #endregion

    private static CesiumMapView.MissionTool ParseMissionTool(string tool) => tool switch
    {
        "waypoint" => CesiumMapView.MissionTool.Waypoint,
        "home" => CesiumMapView.MissionTool.Home,
        "survey" => CesiumMapView.MissionTool.Survey,
        "orbit" => CesiumMapView.MissionTool.Orbit,
        "rtl" => CesiumMapView.MissionTool.Rtl,
        "land" => CesiumMapView.MissionTool.Land,
        _ => CesiumMapView.MissionTool.None
    };

    private void UpdateDroneOnMap(LiveMapPageViewModel vm)
    {
        if (!_isMapReady || _cesiumMap == null || !vm.HasValidPosition)
        {
            Debug.WriteLine($"[LiveMapPage] Cannot update map: MapReady={_isMapReady}, CesiumMap={_cesiumMap != null}, HasValidPosition={vm.HasValidPosition}");
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

    /// <summary>
    /// Update spray overlay on map when spray state changes
    /// </summary>
    public void SetSprayState(bool active, double sprayWidth = 4.0)
    {
        if (!_isMapReady || _cesiumMap == null) return;
        
        Dispatcher.UIThread.Post(() =>
        {
            _cesiumMap.SetSprayActive(active, sprayWidth);
        });
    }

    /// <summary>
    /// Render survey grid on the map
    /// </summary>
    public void RenderSurveyGrid(IEnumerable<(double Lat, double Lon, double Alt)> waypoints, double sprayWidth)
    {
        if (!_isMapReady || _cesiumMap == null) return;

        var gridData = new List<CesiumMapView.SurveyGridData>();
        foreach (var wp in waypoints)
        {
            gridData.Add(new CesiumMapView.SurveyGridData 
            { 
                Lat = wp.Lat, 
                Lon = wp.Lon, 
                Alt = wp.Alt 
            });
        }

        Dispatcher.UIThread.Post(() =>
        {
            _cesiumMap.RenderSurveyGrid(gridData.ToArray(), sprayWidth);
        });
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        _cesiumMap?.ZoomIn();
    }

    private void ZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        _cesiumMap?.ZoomOut();
    }
}
