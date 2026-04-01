using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Web.WebView2.Core;

namespace PavamanDroneConfigurator.UI.Controls;

/// <summary>
/// Production-ready Google Maps control using WebView2.
/// Provides real-time drone telemetry visualization with:
/// - Satellite imagery
/// - Multiple view modes (Top-Down, 3D Chase, Free Roam)
/// - Flight path tracking with altitude
/// - Waypoint visualization
/// - Smooth camera following
/// - Survey grid planning
/// - Spray overlay visualization
/// </summary>
public partial class GoogleMapView : UserControl
{
    private CoreWebView2? _webView;
    private CoreWebView2Controller? _webViewController;
    private nint _webViewHandle;
    private bool _isInitialized;
    private bool _isDisposed;
    private bool _mapReady;
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _boundsChangedHandler;
    private Thickness _viewportInsets = new(0);
    
    // Pending update queue for before map is ready
    private DroneUpdateData? _pendingUpdate;

    // View modes matching JavaScript
    public enum ViewMode
    {
        TopDown,
        Chase3D,
        FreeRoam,
        FirstPerson
    }

    public enum MissionTool
    {
        None,
        Waypoint,
        Home,
        Survey,
        Orbit,
        Rtl,
        Land
    }

    private enum MapMessageType
    {
        mapReady,
        viewModeChanged,
        followChanged,
        error,
        waypoint_placed,
        waypoint_moved,
        waypoint_deleted,
        home_placed,
        land_placed,
        orbit_placed,
        survey_boundary_completed
    }

    private ViewMode _currentViewMode = ViewMode.TopDown;
    private bool _isFollowing = true;

    /// <summary>
    /// Current view mode
    /// </summary>
    public ViewMode CurrentViewMode
    {
        get => _currentViewMode;
        set
        {
            _currentViewMode = value;
            SetViewModeAsync(value);
        }
    }

    /// <summary>
    /// Whether camera is following the drone
    /// </summary>
    public bool IsFollowing
    {
        get => _isFollowing;
        set
        {
            _isFollowing = value;
            if (_mapReady)
            {
                ExecuteScriptAsync($"isFollowing = {value.ToString().ToLowerInvariant()};");
            }
        }
    }

    /// <summary>
    /// Event raised when the map is fully loaded and ready
    /// </summary>
    public event EventHandler? MapReady;

    /// <summary>
    /// Event raised when map initialization fails
    /// </summary>
    public event EventHandler<string>? MapError;

    /// <summary>
    /// Event raised when view mode changes
    /// </summary>
    public event EventHandler<ViewMode>? ViewModeChanged;

    public event EventHandler<(double Latitude, double Longitude, int Index)>? WaypointPlaced;
    public event EventHandler<(int Index, double Latitude, double Longitude)>? WaypointMoved;
    public event EventHandler<int>? WaypointDeleted;
    public event EventHandler<(double Latitude, double Longitude)>? HomePlaced;
    public event EventHandler<(double Latitude, double Longitude)>? LandPlaced;
    public event EventHandler<(double Latitude, double Longitude)>? OrbitPlaced;
    public event EventHandler<List<(double Latitude, double Longitude)>>? SurveyBoundaryCompleted;

    public GoogleMapView()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            // Re-show the WebView and update bounds when re-entering the tab
            if (_webViewController != null)
            {
                _webViewController.IsVisible = true;
                UpdateWebViewBounds();
            }
            return;
        }
        
        try
        {
            await InitializeWebView2Async();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] WebView2 init failed: {ex.Message}");
            ShowError("WebView2 Initialization Failed", ex.Message);
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        // Don't dispose on tab switch - just hide the WebView so state is preserved.
        // The WebView will be shown again when the control is re-loaded.
        if (_webViewController != null)
        {
            _webViewController.IsVisible = false;
        }
    }
    
    private void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            HideError();
            ShowLoading();
            try
            {
                await InitializeWebView2Async();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GoogleMapView] Retry failed: {ex.Message}");
                ShowError("WebView2 Initialization Failed", ex.Message);
            }
        });
    }

    /// <summary>
    /// Handle visibility changes to show/hide WebView2
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == IsVisibleProperty && _webViewController != null)
        {
            _webViewController.IsVisible = IsVisible;
            if (IsVisible)
            {
                UpdateWebViewBounds();
            }
        }
    }

    private async Task InitializeWebView2Async()
    {
        try
        {
            UpdateLoadingText("Checking WebView2 runtime...");
            Debug.WriteLine("[GoogleMapView] Starting WebView2 initialization...");

            // Check if WebView2 runtime is installed
            string? version = null;
            try
            {
                version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                Debug.WriteLine($"[GoogleMapView] WebView2 version: {version}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GoogleMapView] WebView2 not found: {ex.Message}");
                throw new InvalidOperationException(
                    "WebView2 runtime is not installed. Please install Microsoft Edge WebView2 Runtime from https://developer.microsoft.com/microsoft-edge/webview2/");
            }

            UpdateLoadingText("Creating WebView2 environment...");

            // Create environment with user data folder
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PavamanDroneConfigurator", "WebView2");
            
            Directory.CreateDirectory(userDataFolder);
            Debug.WriteLine($"[GoogleMapView] User data folder: {userDataFolder}");

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            Debug.WriteLine("[GoogleMapView] Environment created successfully");
            
            UpdateLoadingText("Initializing Google Maps...");

            // Get the window handle
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is Window window)
            {
                _webViewHandle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                Debug.WriteLine($"[GoogleMapView] Window handle: {_webViewHandle}");
            }

            if (_webViewHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not get window handle for WebView2. Make sure the control is attached to a visible window.");
            }

            // Create WebView2 controller
            Debug.WriteLine("[GoogleMapView] Creating WebView2 controller...");
            _webViewController = await env.CreateCoreWebView2ControllerAsync(_webViewHandle);
            _webView = _webViewController.CoreWebView2;
            Debug.WriteLine("[GoogleMapView] WebView2 controller created successfully");

            // Configure WebView2 settings
            _webView.Settings.IsScriptEnabled = true;
            _webView.Settings.AreDefaultContextMenusEnabled = true; // Enable for debugging
            _webView.Settings.IsStatusBarEnabled = false;
            _webView.Settings.AreDevToolsEnabled = true; // Enable for debugging
            _webView.Settings.IsZoomControlEnabled = false;
            _webView.Settings.IsWebMessageEnabled = true;

            // Handle messages from JavaScript
            _webView.WebMessageReceived += OnWebMessageReceived;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.NavigationStarting += OnNavigationStarting;

            // Position the WebView2 control to fill the parent
            UpdateWebViewBounds();
            Debug.WriteLine($"[GoogleMapView] WebView2 bounds: {_webViewController.Bounds}");
            _webViewController.IsVisible = true;

            // Update bounds when control resizes
            _boundsChangedHandler = (_, e) =>
            {
                if (e.Property == BoundsProperty && _webViewController != null)
                {
                    UpdateWebViewBounds();
                }
            };
            this.PropertyChanged += _boundsChangedHandler;

            // Navigate to Google Maps HTML
            UpdateLoadingText("Loading Google Maps satellite imagery...");
            var mapPath = GetMapHtmlPath();
            
            if (!File.Exists(mapPath))
            {
                throw new FileNotFoundException($"Map HTML file not found at: {mapPath}\nMake sure the Assets/map folder is included in the build.");
            }

            Debug.WriteLine($"[GoogleMapView] Navigating to: {mapPath}");
            var uri = new Uri(mapPath).AbsoluteUri;
            _webView.Navigate(uri);
            _isInitialized = true;

            Debug.WriteLine("[GoogleMapView] Navigation started successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Initialization error: {ex}");
            throw;
        }
    }

    private void UpdateWebViewBounds()
    {
        if (_webViewController == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        // Calculate the position relative to the top-level window
        var origin = this.TranslatePoint(new Point(0, 0), topLevel);
        if (!origin.HasValue)
            return;

        var bounds = this.Bounds;
        
        // Account for DPI scaling if present
        double scaling = 1.0;
        if (topLevel is Window window)
        {
            scaling = window.RenderScaling;
        }

        var insetLeft = _viewportInsets.Left;
        var insetTop = _viewportInsets.Top;
        var insetRight = _viewportInsets.Right;
        var insetBottom = _viewportInsets.Bottom;

        var contentX = origin.Value.X + insetLeft;
        var contentY = origin.Value.Y + insetTop;
        var contentWidth = Math.Max(0, bounds.Width - insetLeft - insetRight);
        var contentHeight = Math.Max(0, bounds.Height - insetTop - insetBottom);

        // Calculate scaled dimensions - WebView2 fills the available viewport area
        var x = (int)Math.Round(contentX * scaling);
        var y = (int)Math.Round(contentY * scaling);
        var width = Math.Max(0, (int)Math.Round(contentWidth * scaling));
        var height = Math.Max(0, (int)Math.Round(contentHeight * scaling));

        // Only update if we have valid dimensions
        if (width > 0 && height > 0)
        {
            _webViewController.Bounds = new System.Drawing.Rectangle(x, y, width, height);
            Debug.WriteLine($"[GoogleMapView] Bounds updated: x={x}, y={y}, w={width}, h={height}, scale={scaling}");
        }
    }

    /// <summary>
    /// Sets viewport insets in device-independent pixels to reserve UI overlay areas.
    /// </summary>
    public void SetViewportInsets(double top, double right, double bottom, double left)
    {
        _viewportInsets = new Thickness(
            Math.Max(0, left),
            Math.Max(0, top),
            Math.Max(0, right),
            Math.Max(0, bottom));

        if (_webViewController != null)
        {
            UpdateWebViewBounds();
        }
    }

    private string GetMapHtmlPath()
    {
        // Try multiple locations - Google Maps HTML only
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "map", "google-map.html"),
            Path.Combine(AppContext.BaseDirectory, "map", "google-map.html"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "map", "google-map.html"),
            Path.Combine(Directory.GetCurrentDirectory(), "map", "google-map.html")
        };

        foreach (var path in candidates)
        {
            Debug.WriteLine($"[GoogleMapView] Checking: {path}");
            if (File.Exists(path))
            {
                Debug.WriteLine($"[GoogleMapView] Found map at: {path}");
                return path;
            }
        }

        Debug.WriteLine("[GoogleMapView] Map HTML not found in any location!");
        return candidates[0];
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        Debug.WriteLine($"[GoogleMapView] Navigation starting: {e.Uri}");
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.IsSuccess)
            {
                Debug.WriteLine("[GoogleMapView] Navigation completed successfully");
                // Map will call back via postMessage when ready
            }
            else
            {
                Debug.WriteLine($"[GoogleMapView] Navigation failed: {e.WebErrorStatus}");
                ShowError("Navigation Failed", $"Failed to load map. Error: {e.WebErrorStatus}\n\nMake sure the Assets/map folder exists and contains google-map.html");
            }
        });
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            Debug.WriteLine($"[GoogleMapView] Message from JS: {message}");

            if (string.IsNullOrEmpty(message)) return;

            var json = JsonDocument.Parse(message);
            var typeString = json.RootElement.GetProperty("type").GetString();
            if (string.IsNullOrWhiteSpace(typeString)) return;

            if (!Enum.TryParse<MapMessageType>(typeString, ignoreCase: true, out var type))
            {
                Debug.WriteLine($"[GoogleMapView] Unknown JS message type: {typeString}");
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                switch (type)
                {
                    case MapMessageType.mapReady:
                        _mapReady = true;
                        HideLoading();
                        Debug.WriteLine("[GoogleMapView] Map is ready!");
                        MapReady?.Invoke(this, EventArgs.Empty);

                        // Send pending update if any
                        if (_pendingUpdate != null)
                        {
                            Debug.WriteLine("[GoogleMapView] Sending pending drone update");
                            UpdateDronePositionInternal(_pendingUpdate);
                            _pendingUpdate = null;
                        }
                        break;

                    case MapMessageType.viewModeChanged:
                        if (json.RootElement.TryGetProperty("mode", out var modeProp))
                        {
                            var modeStr = modeProp.GetString();
                            var mode = modeStr switch
                            {
                                "topdown" => ViewMode.TopDown,
                                "chase3d" or "chase" => ViewMode.Chase3D,
                                "fpv" => ViewMode.FirstPerson,
                                _ => ViewMode.FreeRoam
                            };
                            _currentViewMode = mode;
                            ViewModeChanged?.Invoke(this, mode);
                        }
                        break;

                    case MapMessageType.followChanged:
                        if (json.RootElement.TryGetProperty("following", out var followProp))
                        {
                            _isFollowing = followProp.GetBoolean();
                        }
                        break;
                    
                    case MapMessageType.waypoint_placed:
                        {
                            var lat = json.RootElement.TryGetProperty("lat", out var latProp) ? latProp.GetDouble() : 0;
                            var lon = json.RootElement.TryGetProperty("lon", out var lonProp) ? lonProp.GetDouble() : 0;
                            var idx = json.RootElement.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                            WaypointPlaced?.Invoke(this, (lat, lon, idx));
                            break;
                        }

                    case MapMessageType.waypoint_moved:
                        {
                            var idx = json.RootElement.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                            var lat = json.RootElement.TryGetProperty("lat", out var latProp) ? latProp.GetDouble() : 0;
                            var lon = json.RootElement.TryGetProperty("lon", out var lonProp) ? lonProp.GetDouble() : 0;
                            WaypointMoved?.Invoke(this, (idx, lat, lon));
                            break;
                        }

                    case MapMessageType.waypoint_deleted:
                        {
                            var idx = json.RootElement.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                            WaypointDeleted?.Invoke(this, idx);
                            break;
                        }

                    case MapMessageType.home_placed:
                        {
                            var lat = json.RootElement.TryGetProperty("lat", out var latProp) ? latProp.GetDouble() : 0;
                            var lon = json.RootElement.TryGetProperty("lon", out var lonProp) ? lonProp.GetDouble() : 0;
                            HomePlaced?.Invoke(this, (lat, lon));
                            break;
                        }

                    case MapMessageType.land_placed:
                        {
                            var lat = json.RootElement.TryGetProperty("lat", out var latProp) ? latProp.GetDouble() : 0;
                            var lon = json.RootElement.TryGetProperty("lon", out var lonProp) ? lonProp.GetDouble() : 0;
                            LandPlaced?.Invoke(this, (lat, lon));
                            break;
                        }

                    case MapMessageType.orbit_placed:
                        {
                            var lat = json.RootElement.TryGetProperty("lat", out var latProp) ? latProp.GetDouble() : 0;
                            var lon = json.RootElement.TryGetProperty("lon", out var lonProp) ? lonProp.GetDouble() : 0;
                            OrbitPlaced?.Invoke(this, (lat, lon));
                            break;
                        }

                    case MapMessageType.survey_boundary_completed:
                        {
                            var boundary = new List<(double Latitude, double Longitude)>();
                            if (json.RootElement.TryGetProperty("boundary", out var boundaryProp) && boundaryProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var point in boundaryProp.EnumerateArray())
                                {
                                    var lat = point.TryGetProperty("lat", out var latP) ? latP.GetDouble() : 0;
                                    var lon = point.TryGetProperty("lng", out var lngP) ? lngP.GetDouble() : 0;
                                    boundary.Add((lat, lon));
                                }
                            }
                            SurveyBoundaryCompleted?.Invoke(this, boundary);
                            break;
                        }
                    
                    case MapMessageType.error:
                        if (json.RootElement.TryGetProperty("message", out var errorProp))
                        {
                            var errorMsg = errorProp.GetString();
                            Debug.WriteLine($"[GoogleMapView] JavaScript error: {errorMsg}");
                        }
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error processing message: {ex.Message}");
        }
    }

    /// <summary>
    /// Update drone position with full telemetry data
    /// </summary>
    public void UpdateDronePosition(
        double latitude, 
        double longitude, 
        double altitude, 
        double heading,
        double pitch = 0,
        double roll = 0,
        bool isArmed = false,
        double groundSpeed = 0,
        double verticalSpeed = 0,
        int satelliteCount = 0,
        string flightMode = "Unknown",
        double flowRate = 0)
    {
        var data = new DroneUpdateData
        {
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            Heading = heading,
            Pitch = pitch,
            Roll = roll,
            IsArmed = isArmed,
            GroundSpeed = groundSpeed,
            VerticalSpeed = verticalSpeed,
            SatelliteCount = satelliteCount,
            FlightMode = flightMode,
            FlowRate = flowRate
        };

        if (!_mapReady)
        {
            _pendingUpdate = data;
            Debug.WriteLine("[GoogleMapView] Map not ready yet, storing pending update");
            return;
        }

        UpdateDronePositionInternal(data);
    }

    private async void UpdateDronePositionInternal(DroneUpdateData data)
    {
        if (_webView == null || _isDisposed)
        {
            Debug.WriteLine("[GoogleMapView] Cannot update: WebView not ready or disposed");
            return;
        }

        try
        {
            var flightModeJson = System.Text.Json.JsonSerializer.Serialize(data.FlightMode);
            var script = string.Format(
                CultureInfo.InvariantCulture,
                "updateDrone({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11});",
                data.Latitude,
                data.Longitude,
                data.Altitude,
                data.Heading,
                data.Pitch,
                data.Roll,
                data.IsArmed.ToString().ToLowerInvariant(),
                data.GroundSpeed,
                data.VerticalSpeed,
                data.SatelliteCount,
                flightModeJson,
                data.FlowRate);

            await _webView.ExecuteScriptAsync(script);
            // Only log occasionally to avoid spam
            if (DateTime.Now.Second % 5 == 0)
            {
                Debug.WriteLine($"[GoogleMapView] Updated drone position: {data.Latitude:F6}, {data.Longitude:F6}, {data.Altitude:F1}m");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error updating drone: {ex.Message}");
        }
    }

    /// <summary>
    /// Set view mode
    /// </summary>
    public async void SetViewModeAsync(ViewMode mode)
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            var modeStr = mode switch
            {
                ViewMode.TopDown => "topdown",
                ViewMode.Chase3D => "chase",
                ViewMode.FirstPerson => "fpv",
                _ => "freeroam"
            };

            await _webView.ExecuteScriptAsync($"setViewMode('{modeStr}');");
            _currentViewMode = mode;
            Debug.WriteLine($"[GoogleMapView] View mode set to: {modeStr}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error setting view mode: {ex.Message}");
        }
    }

    /// <summary>
    /// Center camera on drone
    /// </summary>
    public async void CenterOnDrone(bool animate = true)
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            await _webView.ExecuteScriptAsync($"centerOnDrone({animate.ToString().ToLowerInvariant()});");
            Debug.WriteLine("[GoogleMapView] Centered on drone");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error centering: {ex.Message}");
        }
    }

    /// <summary>
    /// Toggle camera follow mode
    /// </summary>
    public async void ToggleFollow()
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            var result = await _webView.ExecuteScriptAsync("toggleFollow();");
            _isFollowing = result.Trim('"').ToLowerInvariant() == "true";
            Debug.WriteLine($"[GoogleMapView] Follow mode: {_isFollowing}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error toggling follow: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear flight path
    /// </summary>
    public async void ClearFlightPath()
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            await _webView.ExecuteScriptAsync("clearFlightPath();");
            Debug.WriteLine("[GoogleMapView] Flight path cleared");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error clearing path: {ex.Message}");
        }
    }

    /// <summary>
    /// Restore flight path from ViewModel data (e.g. after tab switch).
    /// Sends the complete set of coordinates to the JavaScript side so any
    /// points accumulated while the page was hidden are recovered.
    /// </summary>
    public async void RestoreFlightPath(IReadOnlyList<(double Lat, double Lon)> path)
    {
        if (_webView == null || !_mapReady || path.Count == 0) return;

        try
        {
            var coords = path.Select(p => new { lat = p.Lat, lng = p.Lon }).ToArray();
            var json = JsonSerializer.Serialize(coords);
            await _webView.ExecuteScriptAsync($"restoreFlightPath({json});");
            Debug.WriteLine($"[GoogleMapView] Flight path restored with {path.Count} points");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error restoring flight path: {ex.Message}");
        }
    }

    /// <summary>
    /// Set waypoints on the map
    /// </summary>
    public async void SetWaypoints(WaypointData[] waypoints)
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            var waypointsJson = JsonSerializer.Serialize(waypoints);
            await _webView.ExecuteScriptAsync($"setWaypoints({waypointsJson});");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error setting waypoints: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear all waypoints
    /// </summary>
    public async void ClearWaypoints()
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            await _webView.ExecuteScriptAsync("clearWaypoints();");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error clearing waypoints: {ex.Message}");
        }
    }

    /// <summary>
    /// Set the current active waypoint
    /// </summary>
    public async void SetCurrentWaypoint(int index)
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            await _webView.ExecuteScriptAsync($"setCurrentWaypoint({index});");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error setting current waypoint: {ex.Message}");
        }
    }

    /// <summary>
    /// Set geofence visualization
    /// </summary>
    public async void SetGeofence(double centerLat, double centerLon, double radiusMeters, double? maxAltitude = null)
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            var maxAltStr = maxAltitude.HasValue ? maxAltitude.Value.ToString(CultureInfo.InvariantCulture) : "null";
            await _webView.ExecuteScriptAsync(
                $"setGeofence({{lat: {centerLat.ToString(CultureInfo.InvariantCulture)}, lon: {centerLon.ToString(CultureInfo.InvariantCulture)}}}, {radiusMeters.ToString(CultureInfo.InvariantCulture)}, {maxAltStr});");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error setting geofence: {ex.Message}");
        }
    }

    /// <summary>
    /// Set map imagery type
    /// </summary>
    public async void SetMapType(string type)
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            await _webView.ExecuteScriptAsync($"setMapType('{type}');");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error setting map type: {ex.Message}");
        }
    }

    /// <summary>
    /// Set mission tool
    /// </summary>
    public async void SetMissionTool(MissionTool tool)
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            var toolName = tool switch
            {
                MissionTool.Waypoint => "waypoint",
                MissionTool.Home => "home",
                MissionTool.Survey => "survey",
                MissionTool.Orbit => "orbit",
                MissionTool.Rtl => "rtl",
                MissionTool.Land => "land",
                _ => "none"
            };

            await _webView.ExecuteScriptAsync($"setMissionTool('{toolName}');");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error setting mission tool: {ex.Message}");
        }
    }

    /// <summary>
    /// Zoom in
    /// </summary>
    public async void ZoomIn()
    {
        if (_webView == null || !_mapReady) return;
        await ExecuteScriptAsync("zoomIn();");
    }

    /// <summary>
    /// Zoom out
    /// </summary>
    public async void ZoomOut()
    {
        if (_webView == null || !_mapReady) return;
        await ExecuteScriptAsync("zoomOut();");
    }

    /// <summary>
    /// Reset view to home or default position
    /// </summary>
    public async void ResetView()
    {
        if (_webView == null || !_mapReady) return;
        await ExecuteScriptAsync("resetView();");
    }

    /// <summary>
    /// Render survey grid on the map
    /// </summary>
    public async void RenderSurveyGrid(SurveyGridData[] waypoints, double sprayWidth)
    {
        if (_webView == null || !_mapReady) return;

        try
        {
            var waypointsJson = JsonSerializer.Serialize(waypoints);
            await _webView.ExecuteScriptAsync($"renderSurveyGrid({waypointsJson}, {sprayWidth.ToString(CultureInfo.InvariantCulture)});");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Error rendering survey grid: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear survey grid from the map
    /// </summary>
    public async void ClearSurveyGrid()
    {
        if (_webView == null || !_mapReady) return;
        await ExecuteScriptAsync("clearSurveyGrid();");
    }

    /// <summary>
    /// Clear survey boundary
    /// </summary>
    public async void ClearSurveyBoundary()
    {
        if (_webView == null || !_mapReady) return;
        await ExecuteScriptAsync("clearSurveyBoundary();");
    }

    /// <summary>
    /// Set spray active state
    /// </summary>
    public async void SetSprayActive(bool active, double sprayWidth = 4.0)
    {
        if (_webView == null || !_mapReady) return;
        await ExecuteScriptAsync($"setSprayActive({active.ToString().ToLowerInvariant()}, {sprayWidth.ToString(CultureInfo.InvariantCulture)});");
    }

    /// <summary>
    /// Clear spray overlay
    /// </summary>
    public async void ClearSprayOverlay()
    {
        if (_webView == null || !_mapReady) return;
        await ExecuteScriptAsync("clearSprayOverlay();");
    }

    /// <summary>
    /// Delete a waypoint by index
    /// </summary>
    public async void DeleteWaypoint(int index)
    {
        if (_webView == null || !_mapReady) return;
        await ExecuteScriptAsync($"deleteWaypoint({index});");
    }

    /// <summary>
    /// Update battery percentage shown in the in-map HUD card.
    /// </summary>
    public async void UpdateBattery(int batteryPercent)
    {
        if (_webView == null || !_mapReady) return;
        await ExecuteScriptAsync($"updateBattery({batteryPercent});");
    }

    private async Task ExecuteScriptAsync(string script)
    {
        try
        {
            if (_webView != null)
            {
                await _webView.ExecuteScriptAsync(script);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleMapView] Script error: {ex.Message}");
        }
    }

    private void UpdateLoadingText(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<TextBlock>("LoadingText") is { } tb)
            {
                tb.Text = text;
            }
        });
    }

    private void HideLoading()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<Border>("LoadingOverlay") is { } overlay)
            {
                overlay.IsVisible = false;
            }
        });
    }
    
    private void ShowLoading()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<Border>("LoadingOverlay") is { } overlay)
            {
                overlay.IsVisible = true;
            }
            if (this.FindControl<Border>("ErrorOverlay") is { } error)
            {
                error.IsVisible = false;
            }
        });
    }
    
    private void HideError()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<Border>("ErrorOverlay") is { } error)
            {
                error.IsVisible = false;
            }
        });
    }

    private void ShowError(string title, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<Border>("LoadingOverlay") is { } loading)
            {
                loading.IsVisible = false;
            }

            if (this.FindControl<Border>("ErrorOverlay") is { } error)
            {
                error.IsVisible = true;
            }

            if (this.FindControl<TextBlock>("ErrorTitle") is { } titleTb)
            {
                titleTb.Text = title;
            }

            if (this.FindControl<TextBlock>("ErrorMessage") is { } msgTb)
            {
                msgTb.Text = message;
            }

            MapError?.Invoke(this, $"{title}: {message}");
        });
    }

    private void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_boundsChangedHandler != null)
        {
            this.PropertyChanged -= _boundsChangedHandler;
            _boundsChangedHandler = null;
        }

        if (_webView != null)
        {
            _webView.WebMessageReceived -= OnWebMessageReceived;
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _webView.NavigationStarting -= OnNavigationStarting;
        }

        _webViewController?.Close();
    }

    // Data classes
    private class DroneUpdateData
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Altitude { get; set; }
        public double Heading { get; set; }
        public double Pitch { get; set; }
        public double Roll { get; set; }
        public bool IsArmed { get; set; }
        public double GroundSpeed { get; set; }
        public double VerticalSpeed { get; set; }
        public int SatelliteCount { get; set; }
        public string FlightMode { get; set; } = "Unknown";
        public double FlowRate { get; set; }
    }

    public class WaypointData
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }
        
        [JsonPropertyName("lon")]
        public double Lon { get; set; }
        
        [JsonPropertyName("alt")]
        public double Alt { get; set; }
        
        [JsonPropertyName("groundAlt")]
        public double? GroundAlt { get; set; }
        
        [JsonPropertyName("isCurrent")]
        public bool IsCurrent { get; set; }
    }

    public class SurveyGridData
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }
        
        [JsonPropertyName("lon")]
        public double Lon { get; set; }
        
        [JsonPropertyName("alt")]
        public double Alt { get; set; }
    }
}
