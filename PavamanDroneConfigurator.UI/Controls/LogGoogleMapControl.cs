using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using PavamanDroneConfigurator.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.UI.Controls;

/// <summary>
/// Google Maps-based map control for the Log Analyzer.
/// Replaces the old Mapsui/OpenStreetMap LogMapControl with WebView2 + Google Maps.
/// Features: GPS track visualization, crash detection, event markers, waypoint display,
/// flight statistics, coordinate display, map type cycling, zoom-to-fit.
/// </summary>
public class LogGoogleMapControl : UserControl
{
    private CoreWebView2? _webView;
    private CoreWebView2Controller? _webViewController;
    private nint _webViewHandle;
    private bool _isInitialized;
    private bool _isDisposed = false;
    private bool _mapReady;
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _boundsChangedHandler;

    // Pending operations queue (before map ready)
    private List<GpsTrackPoint>? _pendingTrack;
    private List<LogEvent>? _pendingEvents;
    private List<WaypointPoint>? _pendingWaypoints;

    // Performance limits
    private const int MaxTrackPointsForRender = 5000;
    private const int MaxEvents = 200;
    private const int MaxWaypoints = 200;

    // Track whether we need to re-send data after becoming visible
    private bool _needsDataRefreshAfterVisible = false;

    #region Styled Properties

    public static readonly StyledProperty<IEnumerable<GpsTrackPoint>?> TrackPointsProperty =
        AvaloniaProperty.Register<LogGoogleMapControl, IEnumerable<GpsTrackPoint>?>(nameof(TrackPoints));

    public IEnumerable<GpsTrackPoint>? TrackPoints
    {
        get => GetValue(TrackPointsProperty);
        set => SetValue(TrackPointsProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<LogEvent>?> CriticalEventsProperty =
        AvaloniaProperty.Register<LogGoogleMapControl, IEnumerable<LogEvent>?>(nameof(CriticalEvents));

    public IEnumerable<LogEvent>? CriticalEvents
    {
        get => GetValue(CriticalEventsProperty);
        set => SetValue(CriticalEventsProperty, value);
    }

    public static readonly StyledProperty<double> CenterLatitudeProperty =
        AvaloniaProperty.Register<LogGoogleMapControl, double>(nameof(CenterLatitude));

    public double CenterLatitude
    {
        get => GetValue(CenterLatitudeProperty);
        set => SetValue(CenterLatitudeProperty, value);
    }

    public static readonly StyledProperty<double> CenterLongitudeProperty =
        AvaloniaProperty.Register<LogGoogleMapControl, double>(nameof(CenterLongitude));

    public double CenterLongitude
    {
        get => GetValue(CenterLongitudeProperty);
        set => SetValue(CenterLongitudeProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<WaypointPoint>?> WaypointsProperty =
        AvaloniaProperty.Register<LogGoogleMapControl, IEnumerable<WaypointPoint>?>(nameof(Waypoints));

    public IEnumerable<WaypointPoint>? Waypoints
    {
        get => GetValue(WaypointsProperty);
        set => SetValue(WaypointsProperty, value);
    }

    // kept for AXAML compat - unused properties still need to exist
    public static readonly StyledProperty<bool> ShowStatsProperty =
        AvaloniaProperty.Register<LogGoogleMapControl, bool>(nameof(ShowStats), true);
    public bool ShowStats { get => GetValue(ShowStatsProperty); set => SetValue(ShowStatsProperty, value); }

    public static readonly StyledProperty<bool> ShowLegendProperty =
        AvaloniaProperty.Register<LogGoogleMapControl, bool>(nameof(ShowLegend), true);
    public bool ShowLegend { get => GetValue(ShowLegendProperty); set => SetValue(ShowLegendProperty, value); }

    #endregion

    public LogGoogleMapControl()
    {
        // Show a loading placeholder until WebView2 initializes
        Content = new Border
        {
            Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(15, 23, 42)),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Loading Google Maps...",
                        Foreground = Brushes.White,
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        SetupPropertyHandlers();
    }

    private void SetupPropertyHandlers()
    {
        TrackPointsProperty.Changed.AddClassHandler<LogGoogleMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
            {
                if (args.OldValue is INotifyCollectionChanged oldCollection)
                    oldCollection.CollectionChanged -= control.OnTrackPointsCollectionChanged;
                if (args.NewValue is INotifyCollectionChanged newCollection)
                    newCollection.CollectionChanged += control.OnTrackPointsCollectionChanged;
                control.SendTrackToMap();
            }
        });

        CriticalEventsProperty.Changed.AddClassHandler<LogGoogleMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
            {
                if (args.OldValue is INotifyCollectionChanged oldCollection)
                    oldCollection.CollectionChanged -= control.OnCriticalEventsCollectionChanged;
                if (args.NewValue is INotifyCollectionChanged newCollection)
                    newCollection.CollectionChanged += control.OnCriticalEventsCollectionChanged;
                control.SendEventsToMap();
            }
        });

        WaypointsProperty.Changed.AddClassHandler<LogGoogleMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
                control.SendWaypointsToMap();
        });
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isInitialized && !_isDisposed)
        {
            // Tab was switched back - just make visible and update bounds
            if (_webViewController != null)
            {
                _webViewController.IsVisible = true;
                UpdateWebViewBounds();
            }

            // Re-send data if needed (e.g. data arrived while tab was hidden)
            if (_needsDataRefreshAfterVisible && _mapReady)
            {
                _needsDataRefreshAfterVisible = false;
                SendTrackToMapImmediate();
                SendEventsToMapImmediate();
                SendWaypointsToMapImmediate();
            }
            return;
        }

        try
        {
            await InitializeWebView2Async();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogGoogleMapControl] WebView2 init failed: {ex.Message}");
            Content = new Border
            {
                Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(15, 23, 42)),
                Child = new TextBlock
                {
                    Text = $"Map initialization failed: {ex.Message}",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(20)
                }
            };
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        // IMPORTANT: Only hide the WebView, do NOT dispose it.
        // Disposing here causes the map to be permanently destroyed when the user
        // switches tabs. Actual disposal happens in OnUnloaded(RoutedEventArgs e).
        if (_webViewController != null)
            _webViewController.IsVisible = false;
    }

    private async Task InitializeWebView2Async()
    {
        try
        {
            string? version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            Debug.WriteLine($"[LogGoogleMapControl] WebView2 version: {version}");

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PavamanDroneConfigurator", "WebView2_LogMap");
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is Window window)
                _webViewHandle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

            if (_webViewHandle == IntPtr.Zero)
                throw new InvalidOperationException("Could not get window handle for WebView2.");

            _webViewController = await env.CreateCoreWebView2ControllerAsync(_webViewHandle);
            _webView = _webViewController.CoreWebView2;

            _webView.Settings.IsScriptEnabled = true;
            _webView.Settings.AreDefaultContextMenusEnabled = false;
            _webView.Settings.IsStatusBarEnabled = false;
            _webView.Settings.AreDevToolsEnabled = false;
            _webView.Settings.IsZoomControlEnabled = false;
            _webView.Settings.IsWebMessageEnabled = true;

            _webView.WebMessageReceived += OnWebMessageReceived;

            UpdateWebViewBounds();
            _webViewController.IsVisible = true;

            _boundsChangedHandler = (_, e) =>
            {
                if (e.Property == BoundsProperty && _webViewController != null)
                    UpdateWebViewBounds();
            };
            PropertyChanged += _boundsChangedHandler;

            var mapPath = GetMapHtmlPath();
            if (!File.Exists(mapPath))
                throw new FileNotFoundException($"Log map HTML not found at: {mapPath}");

            var uri = new Uri(mapPath).AbsoluteUri;
            _webView.Navigate(uri);
            _isInitialized = true;

            Debug.WriteLine("[LogGoogleMapControl] Initialized successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogGoogleMapControl] Init error: {ex}");
            throw;
        }
    }

    private void UpdateWebViewBounds()
    {
        if (_webViewController == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var origin = this.TranslatePoint(new Point(0, 0), topLevel);
        if (!origin.HasValue) return;

        var bounds = this.Bounds;
        double scaling = topLevel is Window w ? w.RenderScaling : 1.0;

        var x = (int)Math.Round(origin.Value.X * scaling);
        var y = (int)Math.Round(origin.Value.Y * scaling);
        var width = Math.Max(0, (int)Math.Round(bounds.Width * scaling));
        var height = Math.Max(0, (int)Math.Round(bounds.Height * scaling));

        if (width > 0 && height > 0)
            _webViewController.Bounds = new System.Drawing.Rectangle(x, y, width, height);
    }

    private string GetMapHtmlPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "map", "log-map.html"),
            Path.Combine(AppContext.BaseDirectory, "map", "log-map.html"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "map", "log-map.html"),
            Path.Combine(Directory.GetCurrentDirectory(), "map", "log-map.html")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                Debug.WriteLine($"[LogGoogleMapControl] Found map at: {path}");
                return path;
            }
        }

        Debug.WriteLine("[LogGoogleMapControl] log-map.html not found in any location!");
        return candidates[0];
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message)) return;

            var json = JsonDocument.Parse(message);
            var typeString = json.RootElement.GetProperty("type").GetString();

            if (typeString == "mapReady")
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _mapReady = true;
                    Debug.WriteLine("[LogGoogleMapControl] Map is ready!");

                    // Send any pending data
                    if (_pendingTrack != null)
                    {
                        var track = _pendingTrack;
                        _pendingTrack = null;
                        SendTrackToMapInternal(track);
                    }
                    else
                    {
                        // No pending track - check if we have current data to send
                        SendTrackToMapImmediate();
                    }

                    if (_pendingEvents != null)
                    {
                        var events = _pendingEvents;
                        _pendingEvents = null;
                        SendEventsToMapInternal(events);
                    }
                    else
                    {
                        SendEventsToMapImmediate();
                    }

                    if (_pendingWaypoints != null)
                    {
                        var waypoints = _pendingWaypoints;
                        _pendingWaypoints = null;
                        SendWaypointsToMapInternal(waypoints);
                    }
                    else
                    {
                        SendWaypointsToMapImmediate();
                    }
                });
            }
            else if (typeString == "error")
            {
                if (json.RootElement.TryGetProperty("message", out var errorProp))
                    Debug.WriteLine($"[LogGoogleMapControl] JS error: {errorProp.GetString()}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogGoogleMapControl] Error processing message: {ex.Message}");
        }
    }

    #region Data Sending

    /// <summary>
    /// Called when TrackPoints property changes. Sends data to map or queues it as pending.
    /// </summary>
    private void SendTrackToMap()
    {
        if (_isDisposed) return;

        var points = TrackPoints?.Where(p => IsValidCoordinate(p.Latitude, p.Longitude))
            .Take(MaxTrackPointsForRender * 2).ToList();

        if (points == null || points.Count == 0)
        {
            if (_mapReady) ExecuteScript("clearAll();");
            return;
        }

        // Downsample if needed
        if (points.Count > MaxTrackPointsForRender)
            points = DownsamplePoints(points, MaxTrackPointsForRender);

        if (!_mapReady)
        {
            // Store as pending - will be sent when map is ready
            _pendingTrack = points;

            // Mark that we need to refresh when control becomes visible
            if (!IsVisible)
                _needsDataRefreshAfterVisible = true;
            return;
        }

        // If control is not visible (tab switched away), queue data for when it becomes visible
        if (!IsVisible)
        {
            _pendingTrack = points;
            _needsDataRefreshAfterVisible = true;
            return;
        }

        SendTrackToMapInternal(points);
    }

    /// <summary>
    /// Immediately sends the current TrackPoints to the map without any throttle/visibility check.
    /// Used when the map first becomes ready or when the control becomes visible.
    /// </summary>
    private void SendTrackToMapImmediate()
    {
        if (_isDisposed || !_mapReady) return;

        var points = TrackPoints?.Where(p => IsValidCoordinate(p.Latitude, p.Longitude))
            .Take(MaxTrackPointsForRender * 2).ToList();

        if (points == null || points.Count == 0) return;

        if (points.Count > MaxTrackPointsForRender)
            points = DownsamplePoints(points, MaxTrackPointsForRender);

        SendTrackToMapInternal(points);
    }

    private void SendTrackToMapInternal(List<GpsTrackPoint> points)
    {
        try
        {
            var trackData = points.Select(p => new
            {
                lat = Math.Round(p.Latitude, 6),
                lng = Math.Round(p.Longitude, 6),
                alt = Math.Round(p.Altitude, 1),
                ts = Math.Round(p.Timestamp, 2),
                spd = Math.Round(p.Speed, 1)
            }).ToArray();

            var json = JsonSerializer.Serialize(trackData);
            ExecuteScript($"setTrack({json});");

            Debug.WriteLine($"[LogGoogleMapControl] Sent {points.Count} track points to map");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogGoogleMapControl] Error sending track: {ex.Message}");
        }
    }

    private void SendEventsToMap()
    {
        if (_isDisposed) return;

        var events = CriticalEvents?.Where(e => e.HasLocation &&
            IsValidCoordinate(e.Latitude!.Value, e.Longitude!.Value))
            .Take(MaxEvents).ToList();

        if (events == null || events.Count == 0)
        {
            if (_mapReady) ExecuteScript("clearEvents();");
            return;
        }

        if (!_mapReady)
        {
            _pendingEvents = events;
            if (!IsVisible) _needsDataRefreshAfterVisible = true;
            return;
        }

        if (!IsVisible)
        {
            _pendingEvents = events;
            _needsDataRefreshAfterVisible = true;
            return;
        }

        SendEventsToMapInternal(events);
    }

    private void SendEventsToMapImmediate()
    {
        if (_isDisposed || !_mapReady) return;

        var events = CriticalEvents?.Where(e => e.HasLocation &&
            IsValidCoordinate(e.Latitude!.Value, e.Longitude!.Value))
            .Take(MaxEvents).ToList();

        if (events == null || events.Count == 0) return;

        SendEventsToMapInternal(events);
    }

    private void SendEventsToMapInternal(List<LogEvent> events)
    {
        try
        {
            var eventData = events.Select(e => new
            {
                lat = Math.Round(e.Latitude!.Value, 6),
                lng = Math.Round(e.Longitude!.Value, 6),
                sev = e.Severity.ToString(),
                title = e.Title ?? "",
                desc = e.Description ?? "",
                time = e.TimestampDisplay ?? ""
            }).ToArray();

            var json = JsonSerializer.Serialize(eventData);
            ExecuteScript($"setEvents({json});");

            Debug.WriteLine($"[LogGoogleMapControl] Sent {events.Count} events to map");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogGoogleMapControl] Error sending events: {ex.Message}");
        }
    }

    private void SendWaypointsToMap()
    {
        if (_isDisposed) return;

        var waypoints = Waypoints?.Where(w => IsValidCoordinate(w.Latitude, w.Longitude))
            .Take(MaxWaypoints).ToList();

        if (waypoints == null || waypoints.Count == 0)
        {
            if (_mapReady) ExecuteScript("clearWaypoints();");
            return;
        }

        if (!_mapReady)
        {
            _pendingWaypoints = waypoints;
            if (!IsVisible) _needsDataRefreshAfterVisible = true;
            return;
        }

        if (!IsVisible)
        {
            _pendingWaypoints = waypoints;
            _needsDataRefreshAfterVisible = true;
            return;
        }

        SendWaypointsToMapInternal(waypoints);
    }

    private void SendWaypointsToMapImmediate()
    {
        if (_isDisposed || !_mapReady) return;

        var waypoints = Waypoints?.Where(w => IsValidCoordinate(w.Latitude, w.Longitude))
            .Take(MaxWaypoints).ToList();

        if (waypoints == null || waypoints.Count == 0) return;

        SendWaypointsToMapInternal(waypoints);
    }

    private void SendWaypointsToMapInternal(List<WaypointPoint> waypoints)
    {
        try
        {
            var wpData = waypoints.Select(w => new
            {
                lat = Math.Round(w.Latitude, 6),
                lng = Math.Round(w.Longitude, 6),
                label = w.Label ?? ""
            }).ToArray();

            var json = JsonSerializer.Serialize(wpData);
            ExecuteScript($"setWaypoints({json});");

            Debug.WriteLine($"[LogGoogleMapControl] Sent {waypoints.Count} waypoints to map");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogGoogleMapControl] Error sending waypoints: {ex.Message}");
        }
    }

    #endregion

    #region Public Methods

    public void ZoomToTrack()
    {
        if (_mapReady) ExecuteScript("fitToTrack();");
    }

    public void ClearTrack()
    {
        if (_mapReady) ExecuteScript("clearAll();");
    }

    #endregion

    #region Helpers

    private static bool IsValidCoordinate(double lat, double lon)
    {
        return Math.Abs(lat) > 0.001 && Math.Abs(lon) > 0.001 &&
               Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
    }

    private static List<GpsTrackPoint> DownsamplePoints(List<GpsTrackPoint> points, int targetCount)
    {
        if (points.Count <= targetCount) return points;

        var result = new List<GpsTrackPoint>(targetCount);
        var step = (double)(points.Count - 1) / (targetCount - 1);

        for (int i = 0; i < targetCount; i++)
        {
            var index = (int)Math.Round(i * step);
            if (index < points.Count)
                result.Add(points[index]);
        }

        // Always include first and last
        if (result.Count > 0 && result[0] != points[0])
            result[0] = points[0];
        if (result.Count > 1 && result[^1] != points[^1])
            result[^1] = points[^1];

        return result;
    }

    private async void ExecuteScript(string script)
    {
        if (_webView == null || _isDisposed) return;
        try
        {
            await _webView.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogGoogleMapControl] Script error: {ex.Message}");
        }
    }

    #endregion

    #region Event Handlers

    private void OnTrackPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isDisposed) SendTrackToMap();
    }

    private void OnCriticalEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isDisposed) SendEventsToMap();
    }

    #endregion

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && _webViewController != null)
        {
            _webViewController.IsVisible = IsVisible;
            if (IsVisible)
            {
                UpdateWebViewBounds();

                // Re-send data when control becomes visible in case it changed while hidden
                if (_mapReady && _needsDataRefreshAfterVisible)
                {
                    _needsDataRefreshAfterVisible = false;
                    SendTrackToMapImmediate();
                    SendEventsToMapImmediate();
                    SendWaypointsToMapImmediate();
                }
                else if (_mapReady)
                {
                    // Always re-send current data when tab is switched back to ensure map is up to date
                    SendTrackToMapImmediate();
                    SendEventsToMapImmediate();
                    SendWaypointsToMapImmediate();
                }
            }
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        // Do not dispose WebView on tab switch/unload; TabControl unloads non-selected tab content.
        // Disposing here causes markers/waypoints to vanish after switching between Plot and Map tabs.
        if (_webViewController != null)
            _webViewController.IsVisible = false;

        base.OnUnloaded(e);
    }
}
