using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;

namespace PavamanDroneConfigurator.UI.Controls;

/// <summary>
/// Production-ready CesiumJS 3D map control using WebView2.
/// Provides real-time drone telemetry visualization with:
/// - 3D terrain and world imagery
/// - Multiple view modes (Top-Down, 3D Chase, Free Roam)
/// - Flight path tracking with altitude
/// - Waypoint visualization
/// - Smooth camera following
/// </summary>
public partial class CesiumMapView : UserControl
{
    private CoreWebView2? _webView;
    private CoreWebView2Controller? _webViewController;
    private nint _webViewHandle;
    private bool _isInitialized;
    private bool _isDisposed;
    private bool _mapReady;
    
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

    public CesiumMapView()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        
        try
        {
            await InitializeWebView2Async();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CesiumMapView] WebView2 init failed: {ex.Message}");
            ShowError("WebView2 Initialization Failed", ex.Message);
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Dispose();
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
                Debug.WriteLine($"[CesiumMapView] Retry failed: {ex.Message}");
                ShowError("WebView2 Initialization Failed", ex.Message);
            }
        });
    }

    private async Task InitializeWebView2Async()
    {
        try
        {
            UpdateLoadingText("Checking WebView2 runtime...");
            Debug.WriteLine("[CesiumMapView] Starting WebView2 initialization...");

            // Check if WebView2 runtime is installed
            string? version = null;
            try
            {
                version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                Debug.WriteLine($"[CesiumMapView] WebView2 version: {version}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CesiumMapView] WebView2 not found: {ex.Message}");
                throw new InvalidOperationException(
                    "WebView2 runtime is not installed. Please install Microsoft Edge WebView2 Runtime from https://developer.microsoft.com/microsoft-edge/webview2/");
            }

            UpdateLoadingText("Creating WebView2 environment...");

            // Create environment with user data folder
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PavamanDroneConfigurator", "WebView2");
            
            Directory.CreateDirectory(userDataFolder);
            Debug.WriteLine($"[CesiumMapView] User data folder: {userDataFolder}");

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            Debug.WriteLine("[CesiumMapView] Environment created successfully");
            
            UpdateLoadingText("Initializing 3D map engine...");

            // Get the window handle
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is Window window)
            {
                _webViewHandle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                Debug.WriteLine($"[CesiumMapView] Window handle: {_webViewHandle}");
            }

            if (_webViewHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not get window handle for WebView2. Make sure the control is attached to a visible window.");
            }

            // Create WebView2 controller
            Debug.WriteLine("[CesiumMapView] Creating WebView2 controller...");
            _webViewController = await env.CreateCoreWebView2ControllerAsync(_webViewHandle);
            _webView = _webViewController.CoreWebView2;
            Debug.WriteLine("[CesiumMapView] WebView2 controller created successfully");

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
            var bounds = this.Bounds;
            _webViewController.Bounds = new System.Drawing.Rectangle(0, 0, (int)bounds.Width, (int)bounds.Height);
            Debug.WriteLine($"[CesiumMapView] WebView2 bounds: {_webViewController.Bounds}");
            _webViewController.IsVisible = true;

            // Update bounds when control resizes
            this.PropertyChanged += (s, e) =>
            {
                if (e.Property == BoundsProperty && _webViewController != null)
                {
                    var newBounds = this.Bounds;
                    _webViewController.Bounds = new System.Drawing.Rectangle(0, 0, (int)newBounds.Width, (int)newBounds.Height);
                }
            };

            // Navigate to map HTML
            UpdateLoadingText("Loading CesiumJS 3D terrain...");
            var mapPath = GetMapHtmlPath();
            
            if (!File.Exists(mapPath))
            {
                throw new FileNotFoundException($"Map HTML file not found at: {mapPath}\nMake sure the Assets/map folder is included in the build.");
            }

            Debug.WriteLine($"[CesiumMapView] Navigating to: {mapPath}");
            var uri = new Uri(mapPath).AbsoluteUri;
            _webView.Navigate(uri);
            _isInitialized = true;

            Debug.WriteLine("[CesiumMapView] Navigation started successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CesiumMapView] Initialization error: {ex}");
            throw;
        }
    }

    private string GetMapHtmlPath()
    {
        // Try multiple locations
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "map", "index.html"),
            Path.Combine(AppContext.BaseDirectory, "map", "index.html"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "map", "index.html"),
            Path.Combine(Directory.GetCurrentDirectory(), "map", "index.html")
        };

        foreach (var path in candidates)
        {
            Debug.WriteLine($"[CesiumMapView] Checking: {path}");
            if (File.Exists(path))
            {
                Debug.WriteLine($"[CesiumMapView] Found map at: {path}");
                return path;
            }
        }

        Debug.WriteLine("[CesiumMapView] Map HTML not found in any location!");
        return candidates[0];
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        Debug.WriteLine($"[CesiumMapView] Navigation starting: {e.Uri}");
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.IsSuccess)
            {
                Debug.WriteLine("[CesiumMapView] Navigation completed successfully");
                // Map will call back via postMessage when ready
            }
            else
            {
                Debug.WriteLine($"[CesiumMapView] Navigation failed: {e.WebErrorStatus}");
                ShowError("Navigation Failed", $"Failed to load map. Error: {e.WebErrorStatus}\n\nMake sure the Assets/map folder exists and contains index.html, app.js, and styles.css");
            }
        });
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            Debug.WriteLine($"[CesiumMapView] Message from JS: {message}");

            if (string.IsNullOrEmpty(message)) return;

            var json = JsonDocument.Parse(message);
            var type = json.RootElement.GetProperty("type").GetString();

            Dispatcher.UIThread.Post(() =>
            {
                switch (type)
                {
                    case "mapReady":
                        _mapReady = true;
                        HideLoading();
                        Debug.WriteLine("[CesiumMapView] Map is ready!");
                        MapReady?.Invoke(this, EventArgs.Empty);

                        // Send pending update if any
                        if (_pendingUpdate != null)
                        {
                            Debug.WriteLine("[CesiumMapView] Sending pending drone update");
                            UpdateDronePositionInternal(_pendingUpdate);
                            _pendingUpdate = null;
                        }
                        break;

                    case "viewModeChanged":
                        if (json.RootElement.TryGetProperty("mode", out var modeProp))
                        {
                            var modeStr = modeProp.GetString();
                            var mode = modeStr switch
                            {
                                "topdown" => ViewMode.TopDown,
                                "chase3d" => ViewMode.Chase3D,
                                "fpv" => ViewMode.FirstPerson,
                                _ => ViewMode.FreeRoam
                            };
                            _currentViewMode = mode;
                            ViewModeChanged?.Invoke(this, mode);
                        }
                        break;

                    case "followChanged":
                        if (json.RootElement.TryGetProperty("following", out var followProp))
                        {
                            _isFollowing = followProp.GetBoolean();
                        }
                        break;
                    
                    case "error":
                        if (json.RootElement.TryGetProperty("message", out var errorProp))
                        {
                            var errorMsg = errorProp.GetString();
                            Debug.WriteLine($"[CesiumMapView] JavaScript error: {errorMsg}");
                        }
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CesiumMapView] Error processing message: {ex.Message}");
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
        double verticalSpeed = 0)
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
            VerticalSpeed = verticalSpeed
        };

        if (!_mapReady)
        {
            _pendingUpdate = data;
            Debug.WriteLine("[CesiumMapView] Map not ready yet, storing pending update");
            return;
        }

        UpdateDronePositionInternal(data);
    }

    private async void UpdateDronePositionInternal(DroneUpdateData data)
    {
        if (_webView == null || _isDisposed)
        {
            Debug.WriteLine("[CesiumMapView] Cannot update: WebView not ready or disposed");
            return;
        }

        try
        {
            var script = string.Format(
                CultureInfo.InvariantCulture,
                "updateDrone({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8});",
                data.Latitude,
                data.Longitude,
                data.Altitude,
                data.Heading,
                data.Pitch,
                data.Roll,
                data.IsArmed.ToString().ToLowerInvariant(),
                data.GroundSpeed,
                data.VerticalSpeed);

            await _webView.ExecuteScriptAsync(script);
            // Only log occasionally to avoid spam
            if (DateTime.Now.Second % 5 == 0)
            {
                Debug.WriteLine($"[CesiumMapView] Updated drone position: {data.Latitude:F6}, {data.Longitude:F6}, {data.Altitude:F1}m");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CesiumMapView] Error updating drone: {ex.Message}");
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
                ViewMode.Chase3D => "chase3d",
                ViewMode.FirstPerson => "fpv",
                _ => "free"
            };

            await _webView.ExecuteScriptAsync($"setViewMode('{modeStr}');");
            _currentViewMode = mode;
            Debug.WriteLine($"[CesiumMapView] View mode set to: {modeStr}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CesiumMapView] Error setting view mode: {ex.Message}");
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
            Debug.WriteLine("[CesiumMapView] Centered on drone");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CesiumMapView] Error centering: {ex.Message}");
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
            Debug.WriteLine($"[CesiumMapView] Follow mode: {_isFollowing}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CesiumMapView] Error toggling follow: {ex.Message}");
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
            Debug.WriteLine("[CesiumMapView] Flight path cleared");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CesiumMapView] Error clearing path: {ex.Message}");
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
            Debug.WriteLine($"[CesiumMapView] Error setting waypoints: {ex.Message}");
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
            Debug.WriteLine($"[CesiumMapView] Error clearing waypoints: {ex.Message}");
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
            Debug.WriteLine($"[CesiumMapView] Error setting current waypoint: {ex.Message}");
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
            Debug.WriteLine($"[CesiumMapView] Error setting geofence: {ex.Message}");
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
            Debug.WriteLine($"[CesiumMapView] Error setting map type: {ex.Message}");
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
            Debug.WriteLine($"[CesiumMapView] Script error: {ex.Message}");
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
    }

    public class WaypointData
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double Alt { get; set; }
        public double? GroundAlt { get; set; }
        public bool IsCurrent { get; set; }
    }
}