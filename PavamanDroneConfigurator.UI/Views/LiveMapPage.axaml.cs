using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PavamanDroneConfigurator.UI.ViewModels;
using PavamanDroneConfigurator.UI.Controls;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.UI.Views;

/// <summary>
/// Live Map Page - Real-time drone telemetry visualization with Google Maps
/// Supports multiple view modes: Top-Down, 3D Chase, Free Roam
/// Uses Google Maps API for satellite imagery with mission planning
/// </summary>
public partial class LiveMapPage : UserControl
{
    private const float GeofenceVolumeAltitudeThreshold = 0f;

    private GoogleMapView? _map;
    private bool _isInitialized;
    private bool _isMapReady;
    private int _updateCount = 0;
    private ISafetyService? _safetyService;
    private bool _geofenceLoaded;

    public LiveMapPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Debug.WriteLine("[LiveMapPage] Constructor called");
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] OnLoaded - Finding GoogleMap control");

        if (!_isInitialized)
        {
            _map = this.FindControl<GoogleMapView>("MapView");

            if (_map == null)
            {
                Debug.WriteLine("[LiveMapPage] ERROR: GoogleMap control not found!");
                return;
            }

            Debug.WriteLine("[LiveMapPage] GoogleMap control found, setting up event handlers");

            // Reserve space for the Avalonia overlay toolbar (≈52dp top, 0 elsewhere).
            _map.SetViewportInsets(top: 52, right: 0, bottom: 0, left: 0);

            // Subscribe to map events (only once – these are on the control itself)
            _map.MapReady += OnMapReady;
            _map.MapError += OnMapError;
            _map.WaypointPlaced += OnWaypointPlaced;
            _map.WaypointMoved += OnWaypointMoved;
            _map.WaypointDeleted += OnWaypointDeleted;
            _map.HomePlaced += OnHomePlaced;
            _map.LandPlaced += OnLandPlaced;
            _map.OrbitPlaced += OnOrbitPlaced;
            _map.SurveyBoundaryCompleted += OnSurveyBoundaryCompleted;

            _safetyService = App.Services?.GetService<ISafetyService>();
            _isInitialized = true;
        }

        // Always (re-)subscribe to ViewModel events so that telemetry keeps flowing
        // after the control is re-attached to the visual tree (e.g. tab switch).
        if (DataContext is LiveMapPageViewModel vm)
        {
            Debug.WriteLine($"[LiveMapPage] ViewModel connected. IsConnected={vm.IsConnected}, HasValidPosition={vm.HasValidPosition}");

            vm.PositionUpdated += OnPositionUpdated;
            vm.BatteryUpdated += OnBatteryUpdated;
            vm.FlightPathCleared += OnFlightPathCleared;
            vm.RecenterRequested += OnRecenterRequested;
            vm.ViewModeChanged += OnViewModeChanged;
            vm.MapTypeChanged += OnMapTypeChanged;
            vm.FollowChanged += OnFollowChanged;
            vm.MissionToolChanged += OnMissionToolChanged;
            vm.MissionItems.CollectionChanged += OnMissionItemsCollectionChanged;
            vm.OpenMavlinkLogsRequested += OnOpenMavlinkLogsRequested;
            vm.OpenLiveCameraRequested += OnOpenLiveCameraRequested;

            Debug.WriteLine("[LiveMapPage] All event handlers connected");

            // Restore flight path and waypoints when re-entering the tab.
            // This covers any points accumulated while the page was hidden.
            if (_isMapReady && _map != null)
            {
                if (vm.FlightPath.Count > 0)
                {
                    _map.RestoreFlightPath(vm.FlightPath);
                }

                SyncMissionOverlays(vm);

                if (vm.HasValidPosition)
                {
                    UpdateDroneOnMap(vm);
                }
            }
        }
        else
        {
            Debug.WriteLine("[LiveMapPage] WARNING: DataContext is not LiveMapPageViewModel");
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] OnUnloaded");

        // Unsubscribe only ViewModel events (they are re-subscribed in OnLoaded).
        // Map control events stay connected because the control itself persists.
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.PositionUpdated -= OnPositionUpdated;
            vm.BatteryUpdated -= OnBatteryUpdated;
            vm.FlightPathCleared -= OnFlightPathCleared;
            vm.RecenterRequested -= OnRecenterRequested;
            vm.ViewModeChanged -= OnViewModeChanged;
            vm.MapTypeChanged -= OnMapTypeChanged;
            vm.FollowChanged -= OnFollowChanged;
            vm.MissionToolChanged -= OnMissionToolChanged;
            vm.MissionItems.CollectionChanged -= OnMissionItemsCollectionChanged;
            vm.OpenMavlinkLogsRequested -= OnOpenMavlinkLogsRequested;
            vm.OpenLiveCameraRequested -= OnOpenLiveCameraRequested;
        }
    }

    private void OnMapReady(object? sender, EventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] ? Google Maps is ready!");
        _isMapReady = true;

        if (DataContext is LiveMapPageViewModel vm)
        {
            // Initialize map settings from ViewModel
            _map?.SetMapType(vm.GetMapTypeName());
            _map?.SetMissionTool(ParseMissionTool(vm.ActiveMissionTool));
            
            if (_map != null)
            {
                _map.IsFollowing = vm.FollowDrone;
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

            // Restore flight path if we have any accumulated points
            if (vm.FlightPath.Count > 0 && _map != null)
            {
                _map.RestoreFlightPath(vm.FlightPath);
                Debug.WriteLine($"[LiveMapPage] Restored {vm.FlightPath.Count} flight path points on map ready");
            }

            SyncMissionOverlays(vm);
            _ = LoadAndRenderGeofenceAsync(vm);
        }
    }

    private void OnMapError(object? sender, string error)
    {
        Debug.WriteLine($"[LiveMapPage] ? Map error: {error}");
        _isMapReady = false;
    }

    private void OnBatteryUpdated(object? sender, int batteryPercent)
    {
        if (!_isMapReady || _map == null) return;
        _map.UpdateBattery(batteryPercent);
    }

    private void OnPositionUpdated(object? sender, DronePositionUpdateEventArgs e)
    {
        if (!_isMapReady || _map == null)
        {
            return;
        }

        _updateCount++;

        // Log every 20th update to avoid spam
        if (_updateCount % 20 == 0)
        {
            Debug.WriteLine($"[LiveMapPage] Position update #{_updateCount}: Lat={e.Latitude:F6}, Lon={e.Longitude:F6}, Alt={e.Altitude:F1}m, Hdg={e.Heading:F0}�");
        }

        // Use Post for non-blocking UI update
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _map?.UpdateDronePosition(
                    e.Latitude,
                    e.Longitude,
                    e.Altitude,
                    e.Heading,
                    e.Pitch,
                    e.Roll,
                    e.IsArmed,
                    e.GroundSpeed,
                    e.VerticalSpeed,
                    e.SatelliteCount,
                    e.FlightMode,
                    e.FlowRate
                );

                if (!_geofenceLoaded && DataContext is LiveMapPageViewModel vm)
                {
                    _ = LoadAndRenderGeofenceAsync(vm);
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
        if (!_isMapReady || _map == null) return;

        Debug.WriteLine($"[LiveMapPage] View mode changing to: {e.ViewMode}");

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // Map ViewModel view mode strings to GoogleMapView.ViewMode enum
                var viewMode = e.ViewMode switch
                {
                    "topdown" => GoogleMapView.ViewMode.TopDown,
                    "chase3d" or "chase" => GoogleMapView.ViewMode.Chase3D,
                    "fpv" => GoogleMapView.ViewMode.FirstPerson,
                    "free" or "freeroam" => GoogleMapView.ViewMode.FreeRoam,
                    _ => GoogleMapView.ViewMode.TopDown
                };

                _map.CurrentViewMode = viewMode;
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
        if (!_isMapReady || _map == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _map.SetMapType(mapType);
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
        if (!_isMapReady || _map == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _map.IsFollowing = follow;
            Debug.WriteLine($"[LiveMapPage] Follow mode set to: {follow}");
        });
    }

    private void OnMissionToolChanged(object? sender, string tool)
    {
        if (!_isMapReady || _map == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _map.SetMissionTool(ParseMissionTool(tool));
            Debug.WriteLine($"[LiveMapPage] Mission tool set to: {tool}");
        });
    }

    private void OnFlightPathCleared(object? sender, EventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] Flight path clear requested");

        Dispatcher.UIThread.Post(() =>
        {
            _map?.ClearFlightPath();
            _map?.ClearWaypoints();
            _map?.ClearSprayOverlay();
            _geofenceLoaded = false;
            Debug.WriteLine("[LiveMapPage] ? Flight path and waypoints cleared");
        });
    }

    private void OnMissionItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is LiveMapPageViewModel vm)
        {
            SyncMissionOverlays(vm);
        }
    }

    private void OnRecenterRequested(object? sender, EventArgs e)
    {
        Debug.WriteLine("[LiveMapPage] Recenter requested");

        Dispatcher.UIThread.Post(() =>
        {
            _map?.CenterOnDrone(true);
            Debug.WriteLine("[LiveMapPage] ? Recentered on drone");
        });
    }

    private void OnOpenMavlinkLogsRequested(object? sender, EventArgs e)
    {
        if (App.Services == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var logsVm = App.Services.GetRequiredService<MavlinkLogsViewModel>();
                var logsWindow = new MavlinkLogsWindow { DataContext = logsVm };
                var parentWindow = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
                logsWindow.Show(parentWindow);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiveMapPage] Failed to open MAVLink logs: {ex.Message}");
            }
        });
    }

    private void OnOpenLiveCameraRequested(object? sender, EventArgs e)
    {
        if (App.Services == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var cameraVm = App.Services.GetRequiredService<LiveCameraViewModel>();
                var cameraWindow = new LiveCameraWindow { DataContext = cameraVm };
                var parentWindow = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
                cameraWindow.Show(parentWindow);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiveMapPage] Failed to open Live Camera: {ex.Message}");
            }
        });
    }

    #region Mission Planning Events

    private void OnWaypointPlaced(object? sender, (double Latitude, double Longitude, int Index) e)
    {
        Debug.WriteLine($"[LiveMapPage] Waypoint placed: #{e.Index} at {e.Latitude:F6}, {e.Longitude:F6}");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.RegisterMissionItem("WAYPOINT", e.Latitude, e.Longitude);
            SyncMissionOverlays(vm);
        }
    }

    private void OnWaypointMoved(object? sender, (int Index, double Latitude, double Longitude) e)
    {
        Debug.WriteLine($"[LiveMapPage] Waypoint moved: #{e.Index} to {e.Latitude:F6}, {e.Longitude:F6}");
        if (DataContext is LiveMapPageViewModel vm && e.Index >= 0 && e.Index < vm.MissionItems.Count)
        {
            vm.MissionItems[e.Index].Latitude = e.Latitude;
            vm.MissionItems[e.Index].Longitude = e.Longitude;
            vm.RefreshMissionStats();
            SyncMissionOverlays(vm);
        }
    }

    private void OnWaypointDeleted(object? sender, int index)
    {
        Debug.WriteLine($"[LiveMapPage] Waypoint deleted: #{index}");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.RemoveMissionItemAt(index);
            SyncMissionOverlays(vm);
        }
    }

    private void OnHomePlaced(object? sender, (double Latitude, double Longitude) e)
    {
        Debug.WriteLine($"[LiveMapPage] Home placed at {e.Latitude:F6}, {e.Longitude:F6}");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.RegisterMissionItem("HOME", e.Latitude, e.Longitude, 0);
            SyncMissionOverlays(vm);
        }
    }

    private void OnLandPlaced(object? sender, (double Latitude, double Longitude) e)
    {
        Debug.WriteLine($"[LiveMapPage] Land position placed at {e.Latitude:F6}, {e.Longitude:F6}");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.RegisterMissionItem("LAND", e.Latitude, e.Longitude, 0);
            SyncMissionOverlays(vm);
        }
    }

    private void OnOrbitPlaced(object? sender, (double Latitude, double Longitude) e)
    {
        Debug.WriteLine($"[LiveMapPage] Orbit center placed at {e.Latitude:F6}, {e.Longitude:F6}");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            vm.RegisterMissionItem("ORBIT", e.Latitude, e.Longitude);
            SyncMissionOverlays(vm);
        }
    }

    private void OnSurveyBoundaryCompleted(object? sender, List<(double Latitude, double Longitude)> boundary)
    {
        Debug.WriteLine($"[LiveMapPage] Survey boundary completed with {boundary.Count} vertices");
        
        if (DataContext is LiveMapPageViewModel vm)
        {
            // Register survey as a mission item
            var centroid = CalculateCentroid(boundary);
            if (centroid.HasValue)
            {
                vm.RegisterMissionItem("SURVEY", centroid.Value.Lat, centroid.Value.Lon);
                SyncMissionOverlays(vm);
            }
            
            // TODO: Generate survey grid and render on map
            // This would integrate with IMissionService.GenerateSurveyGrid()
        }
    }

    #endregion

    private static GoogleMapView.MissionTool ParseMissionTool(string tool) => tool switch
    {
        "waypoint" => GoogleMapView.MissionTool.Waypoint,
        "home" => GoogleMapView.MissionTool.Home,
        "survey" => GoogleMapView.MissionTool.Survey,
        "orbit" => GoogleMapView.MissionTool.Orbit,
        "rtl" => GoogleMapView.MissionTool.Rtl,
        "land" => GoogleMapView.MissionTool.Land,
        _ => GoogleMapView.MissionTool.None
    };

    private (double Lat, double Lon)? CalculateCentroid(IReadOnlyList<(double Latitude, double Longitude)> boundary)
    {
        if (boundary.Count < 3)
            return null;

        var radians = boundary
            .Select(p => (Lat: DegreesToRadians(p.Latitude), Lon: DegreesToRadians(p.Longitude)))
            .ToList();

        double x = 0;
        double y = 0;
        double z = 0;

        foreach (var point in radians)
        {
            var cosLat = Math.Cos(point.Lat);
            x += cosLat * Math.Cos(point.Lon);
            y += cosLat * Math.Sin(point.Lon);
            z += Math.Sin(point.Lat);
        }

        x /= radians.Count;
        y /= radians.Count;
        z /= radians.Count;

        var lon = Math.Atan2(y, x);
        var hyp = Math.Sqrt((x * x) + (y * y));
        var lat = Math.Atan2(z, hyp);

        return (RadiansToDegrees(lat), RadiansToDegrees(lon));
    }

    private void SyncMissionOverlays(LiveMapPageViewModel vm)
    {
        if (!_isMapReady || _map == null)
            return;

        var waypointData = new List<GoogleMapView.WaypointData>();
        foreach (var item in vm.MissionItems)
        {
            var renderAltitude = item.Command == MissionCommandType.NavWaypoint && item.Altitude <= 0
                ? vm.DefaultAltitude
                : item.Altitude;

            waypointData.Add(new GoogleMapView.WaypointData
            {
                Lat = item.Latitude,
                Lon = item.Longitude,
                Alt = renderAltitude,
                IsCurrent = item.IsCurrent
            });
        }

        _map.SetWaypoints(waypointData.ToArray());
    }

    private async System.Threading.Tasks.Task LoadAndRenderGeofenceAsync(LiveMapPageViewModel vm)
    {
        if (!_isMapReady || _map == null || _safetyService == null)
            return;

        try
        {
            var geofence = await _safetyService.GetGeofenceSettingsAsync();
            if (!geofence.Enabled || geofence.Radius <= 0)
                return;

            if (!vm.HasValidPosition)
                return;

            double? geofenceMaxAltitude = geofence.AltMax > GeofenceVolumeAltitudeThreshold ? geofence.AltMax : null;
            _map.SetGeofence(vm.Latitude, vm.Longitude, geofence.Radius, geofenceMaxAltitude);
            _geofenceLoaded = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LiveMapPage] Failed to render geofence: {ex.Message}");
        }
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
    private static double RadiansToDegrees(double radians) => radians * 180d / Math.PI;

    private void UpdateDroneOnMap(LiveMapPageViewModel vm)
    {
        if (!_isMapReady || _map == null || !vm.HasValidPosition)
        {
            Debug.WriteLine($"[LiveMapPage] Cannot update map: MapReady={_isMapReady}, GoogleMap={_map != null}, HasValidPosition={vm.HasValidPosition}");
            return;
        }

        Debug.WriteLine($"[LiveMapPage] Updating drone on map: {vm.Latitude:F6}, {vm.Longitude:F6}, {vm.Altitude:F1}m");

        _map.UpdateDronePosition(
            vm.Latitude,
            vm.Longitude,
            vm.Altitude,
            vm.Heading,
            vm.Pitch,
            vm.Roll,
            vm.IsArmed,
            vm.GroundSpeed,
            vm.VerticalSpeed,
            vm.SatelliteCount,
            vm.FlightMode,
            0d // FlowRate: spray removed from Live Map
        );
    }

    /// <summary>
    /// Render survey grid on the map
    /// </summary>
    public void RenderSurveyGrid(IEnumerable<(double Lat, double Lon, double Alt)> waypoints, double sprayWidth)
    {
        if (!_isMapReady || _map == null) return;

        var gridData = new List<GoogleMapView.SurveyGridData>();
        foreach (var wp in waypoints)
        {
            gridData.Add(new GoogleMapView.SurveyGridData 
            { 
                Lat = wp.Lat, 
                Lon = wp.Lon, 
                Alt = wp.Alt 
            });
        }

        Dispatcher.UIThread.Post(() =>
        {
            _map.RenderSurveyGrid(gridData.ToArray(), sprayWidth);
        });
    }
}
