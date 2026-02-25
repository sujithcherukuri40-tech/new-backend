using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Providers;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaColor = Avalonia.Media.Color;
using MapsuiColor = Mapsui.Styles.Color;
using MapsuiPen = Mapsui.Styles.Pen;
using MapsuiBrush = Mapsui.Styles.Brush;
using GeoPoint = NetTopologySuite.Geometries.Point;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.UI.Controls;

/// <summary>
/// Industrial-grade Map control using Mapsui with OpenStreetMap (100% Free).
/// Features: GPS track visualization, crash detection (red lines), map controls,
/// altitude/speed data, event markers with color coding, proper refresh handling.
/// OPTIMIZED: Thread-safe, memory-efficient, fast rendering with downsampling.
/// </summary>
public class LogMapControl : UserControl
{
    private readonly MapControl _mapControl;
    private readonly Map _map;
    private WritableLayer? _trackLayer;
    private WritableLayer? _crashLayer;
    private WritableLayer? _markerLayer;
    private WritableLayer? _eventLayer;
    private WritableLayer? _currentPositionLayer;
    private WritableLayer? _waypointLayer;
    private TileLayer? _osmLayer;
    
    // UI Controls
    private TextBlock? _statsPanel;
    private TextBlock? _coordsDisplay;
    private Border? _legendPanel;
    private Border? _crashAlert;
    
    // Data with thread-safe access
    private List<GpsTrackPoint> _trackPoints = new();
    private List<(GpsTrackPoint Start, GpsTrackPoint End)> _crashSegments = new();
    private List<WaypointPoint> _waypoints = new();
    private readonly object _dataLock = new();
    
    // Refresh management - PRODUCTION-READY with proper disposal handling
    private volatile CancellationTokenSource? _refreshCts;
    private readonly object _refreshLock = new();
    private volatile bool _isRefreshing;
    private volatile bool _isDisposed;
    private DateTime _lastRefresh = DateTime.MinValue;
    private const int MinRefreshIntervalMs = 50; // Faster refresh
    
    // Performance limits - OPTIMIZED for fast rendering
    private const int MaxTrackPointsForRender = 2000; // Downsample larger datasets
    private const int MaxTrackPointsStored = 10000;
    private const int MaxEvents = 100; // Limit event markers
    private const int MaxCrashSegments = 20;
    private const int MaxWaypoints = 100;
    
    #region Styled Properties

    public static readonly StyledProperty<IEnumerable<GpsTrackPoint>?> TrackPointsProperty =
        AvaloniaProperty.Register<LogMapControl, IEnumerable<GpsTrackPoint>?>(nameof(TrackPoints));

    public IEnumerable<GpsTrackPoint>? TrackPoints
    {
        get => GetValue(TrackPointsProperty);
        set => SetValue(TrackPointsProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<LogEvent>?> CriticalEventsProperty =
        AvaloniaProperty.Register<LogMapControl, IEnumerable<LogEvent>?>(nameof(CriticalEvents));

    public IEnumerable<LogEvent>? CriticalEvents
    {
        get => GetValue(CriticalEventsProperty);
        set => SetValue(CriticalEventsProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<LogEvent>?> AllEventsProperty =
        AvaloniaProperty.Register<LogMapControl, IEnumerable<LogEvent>?>(nameof(AllEvents));

    public IEnumerable<LogEvent>? AllEvents
    {
        get => GetValue(AllEventsProperty);
        set => SetValue(AllEventsProperty, value);
    }

    public static readonly StyledProperty<double> CenterLatitudeProperty =
        AvaloniaProperty.Register<LogMapControl, double>(nameof(CenterLatitude));

    public double CenterLatitude
    {
        get => GetValue(CenterLatitudeProperty);
        set => SetValue(CenterLatitudeProperty, value);
    }

    public static readonly StyledProperty<double> CenterLongitudeProperty =
        AvaloniaProperty.Register<LogMapControl, double>(nameof(CenterLongitude));

    public double CenterLongitude
    {
        get => GetValue(CenterLongitudeProperty);
        set => SetValue(CenterLongitudeProperty, value);
    }

    public static readonly StyledProperty<double> CurrentPositionTimestampProperty =
        AvaloniaProperty.Register<LogMapControl, double>(nameof(CurrentPositionTimestamp));

    public double CurrentPositionTimestamp
    {
        get => GetValue(CurrentPositionTimestampProperty);
        set => SetValue(CurrentPositionTimestampProperty, value);
    }

    public static readonly StyledProperty<bool> ShowStatsProperty =
        AvaloniaProperty.Register<LogMapControl, bool>(nameof(ShowStats), true);

    public bool ShowStats
    {
        get => GetValue(ShowStatsProperty);
        set => SetValue(ShowStatsProperty, value);
    }

    public static readonly StyledProperty<bool> ShowLegendProperty =
        AvaloniaProperty.Register<LogMapControl, bool>(nameof(ShowLegend), true);

    public bool ShowLegend
    {
        get => GetValue(ShowLegendProperty);
        set => SetValue(ShowLegendProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<WaypointPoint>?> WaypointsProperty =
        AvaloniaProperty.Register<LogMapControl, IEnumerable<WaypointPoint>?>(nameof(Waypoints));

    public IEnumerable<WaypointPoint>? Waypoints
    {
        get => GetValue(WaypointsProperty);
        set => SetValue(WaypointsProperty, value);
    }

    #endregion

    public LogMapControl()
    {
        _map = new Map();
        _mapControl = new MapControl { Map = _map };

        BuildControlUI();
        InitializeMap();
        SetupPropertyHandlers();
    }

    private void BuildControlUI()
    {
        var mainGrid = new Grid();

        mainGrid.Children.Add(_mapControl);

        // Stats panel (top-right)
        var statsPanel = CreateStatsPanel();
        statsPanel.HorizontalAlignment = HorizontalAlignment.Right;
        statsPanel.VerticalAlignment = VerticalAlignment.Top;
        statsPanel.Margin = new Thickness(12);
        mainGrid.Children.Add(statsPanel);

        // Control buttons (right side)
        var controlsPanel = CreateControlsPanel();
        controlsPanel.HorizontalAlignment = HorizontalAlignment.Right;
        controlsPanel.VerticalAlignment = VerticalAlignment.Center;
        controlsPanel.Margin = new Thickness(12);
        mainGrid.Children.Add(controlsPanel);

        // Legend (bottom-left)
        _legendPanel = CreateLegendPanel();
        _legendPanel.HorizontalAlignment = HorizontalAlignment.Left;
        _legendPanel.VerticalAlignment = VerticalAlignment.Bottom;
        _legendPanel.Margin = new Thickness(12, 12, 12, 40);
        _legendPanel.IsVisible = false;
        mainGrid.Children.Add(_legendPanel);

        // Coordinates display (bottom-center)
        _coordsDisplay = new TextBlock
        {
            Text = "Move mouse over map",
            FontSize = 11,
            Foreground = new SolidColorBrush(AvaloniaColor.FromRgb(100, 100, 100)),
            Background = new SolidColorBrush(AvaloniaColor.FromArgb(230, 255, 255, 255)),
            Padding = new Thickness(8, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 12)
        };
        mainGrid.Children.Add(_coordsDisplay);

        // Crash alert (top-center)
        _crashAlert = new Border
        {
            Background = new SolidColorBrush(AvaloniaColor.FromRgb(220, 38, 38)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 60, 0, 0),
            IsVisible = false,
            Child = new TextBlock
            {
                Text = "\u26A0 CRASH DETECTED",
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                FontSize = 14
            }
        };
        mainGrid.Children.Add(_crashAlert);

        Content = mainGrid;
        _mapControl.PointerMoved += OnMapPointerMoved;
    }

    private Border CreateStatsPanel()
    {
        _statsPanel = new TextBlock
        {
            Text = "Flight Statistics\n\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\nLoad a log to see stats",
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, monospace"),
            Foreground = new SolidColorBrush(AvaloniaColor.FromRgb(226, 232, 240))
        };

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaColor.FromArgb(240, 15, 23, 42)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            MinWidth = 180,
            Child = _statsPanel,
            IsVisible = false
        };
    }

    private StackPanel CreateControlsPanel()
    {
        var panel = new StackPanel { Spacing = 6 };

        var buttons = new (string Content, string Tooltip, Action OnClick)[]
        {
            ("+", "Zoom In", () => _map.Navigator?.ZoomIn()),
            ("\u2212", "Zoom Out", () => _map.Navigator?.ZoomOut()),
            ("\u2302", "Fit to Track", () => ZoomToTrack()),
            ("\u25CF", "Legend", () => { if (_legendPanel != null) _legendPanel.IsVisible = !_legendPanel.IsVisible; })
        };

        foreach (var (content, tooltip, onClick) in buttons)
        {
            var btn = new Button
            {
                Content = content,
                Width = 32,
                Height = 32,
                FontSize = 14,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(AvaloniaColor.FromArgb(245, 255, 255, 255)),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            btn.Click += (s, e) => onClick();
            ToolTip.SetTip(btn, tooltip);
            panel.Children.Add(btn);
        }

        return panel;
    }

    private Border CreateLegendPanel()
    {
        var stack = new StackPanel { Spacing = 6 };

        stack.Children.Add(new TextBlock { Text = "Legend", FontWeight = FontWeight.Bold, FontSize = 12 });

        var items = new (string Color, string Label)[]
        {
            ("#3B82F6", "Flight Path"),
            ("#DC2626", "Crash/Emergency"),
            ("#22C55E", "Start Point"),
            ("#EF4444", "End Point"),
            ("#FFA500", "Waypoint"),
            ("#F59E0B", "? Warning Event"),
            ("#DC2626", "?? Critical Event")
        };

        foreach (var (color, label) in items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            
            // Triangle for event markers, circle for others
            var isEvent = label.Contains("Event");
            
            row.Children.Add(new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = isEvent ? new CornerRadius(0) : new CornerRadius(6),
                Background = new SolidColorBrush(AvaloniaColor.Parse(color)),
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = label, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(row);
        }

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaColor.FromArgb(245, 255, 255, 255)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Child = stack
        };
    }

    private void SetupPropertyHandlers()
    {
        TrackPointsProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
            {
                if (args.OldValue is INotifyCollectionChanged oldCollection)
                    oldCollection.CollectionChanged -= control.OnTrackPointsCollectionChanged;
                if (args.NewValue is INotifyCollectionChanged newCollection)
                    newCollection.CollectionChanged += control.OnTrackPointsCollectionChanged;
                control.ScheduleRefreshSafe(() => control.UpdateTrackFast());
            }
        });

        CriticalEventsProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
            {
                if (args.OldValue is INotifyCollectionChanged oldCollection)
                    oldCollection.CollectionChanged -= control.OnCriticalEventsCollectionChanged;
                if (args.NewValue is INotifyCollectionChanged newCollection)
                    newCollection.CollectionChanged += control.OnCriticalEventsCollectionChanged;
                control.ScheduleRefreshSafe(() => control.UpdateEventsFast());
            }
        });

        WaypointsProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
                control.ScheduleRefreshSafe(() => control.UpdateWaypointsFast());
        });
    }
    
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        if (TrackPoints is INotifyCollectionChanged trackCollection)
            trackCollection.CollectionChanged += OnTrackPointsCollectionChanged;
        
        if (CriticalEvents is INotifyCollectionChanged eventsCollection)
            eventsCollection.CollectionChanged += OnCriticalEventsCollectionChanged;
        
        if (TrackPoints?.Any() == true)
            ScheduleRefreshSafe(() => UpdateTrackFast());
        
        if (CriticalEvents?.Any() == true)
            ScheduleRefreshSafe(() => UpdateEventsFast());
        
        if (Waypoints?.Any() == true)
            ScheduleRefreshSafe(() => UpdateWaypointsFast());
    }

    /// <summary>
    /// Production-safe refresh scheduling with proper CancellationTokenSource management.
    /// Prevents ObjectDisposedException crashes.
    /// </summary>
    private void ScheduleRefreshSafe(Action refreshAction)
    {
        if (_isDisposed) return;

        CancellationTokenSource? newCts = null;
        CancellationTokenSource? oldCts = null;

        lock (_refreshLock)
        {
            if (_isDisposed) return;

            // Store old CTS for disposal outside lock
            oldCts = _refreshCts;
            
            // Create new CTS
            newCts = new CancellationTokenSource();
            _refreshCts = newCts;
        }

        // Cancel and dispose old CTS outside lock to prevent deadlocks
        try
        {
            oldCts?.Cancel();
        }
        catch (ObjectDisposedException) { }

        try
        {
            oldCts?.Dispose();
        }
        catch (ObjectDisposedException) { }

        // Calculate delay
        var delay = Math.Max(0, MinRefreshIntervalMs - (int)(DateTime.Now - _lastRefresh).TotalMilliseconds);
        var tokenToUse = newCts.Token;

        // Schedule the refresh
        Task.Delay(delay).ContinueWith(_ =>
        {
            // Early exit checks
            if (_isDisposed) return;
            
            try
            {
                if (tokenToUse.IsCancellationRequested) return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            
            Dispatcher.UIThread.Post(() =>
            {
                if (_isDisposed || _isRefreshing) return;
                
                try
                {
                    if (tokenToUse.IsCancellationRequested) return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                
                try
                {
                    _isRefreshing = true;
                    _lastRefresh = DateTime.Now;
                    refreshAction();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Map refresh error: {ex.Message}");
                }
                finally
                {
                    _isRefreshing = false;
                }
            });
        }, TaskScheduler.Default);
    }

    private void InitializeMap()
    {
        try
        {
            _osmLayer = OpenStreetMap.CreateTileLayer();
            _osmLayer.Name = "OpenStreetMap";
            _map.Layers.Add(_osmLayer);

            _trackLayer = new WritableLayer { Name = "GPS Track" };
            _crashLayer = new WritableLayer { Name = "Crash Segments" };
            _markerLayer = new WritableLayer { Name = "Markers" };
            _eventLayer = new WritableLayer { Name = "Events" };
            _currentPositionLayer = new WritableLayer { Name = "Current Position" };
            _waypointLayer = new WritableLayer { Name = "Waypoints" };

            _map.Layers.Add(_trackLayer);
            _map.Layers.Add(_crashLayer);
            _map.Layers.Add(_waypointLayer);
            _map.Layers.Add(_markerLayer);
            _map.Layers.Add(_eventLayer);
            _map.Layers.Add(_currentPositionLayer);

            _map.Navigator?.CenterOn(0, 0);
            _map.Navigator?.ZoomTo(2);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing map: {ex.Message}");
        }
    }

    #region Map Events

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDisposed || _coordsDisplay == null) return;

        try
        {
            var point = e.GetPosition(_mapControl);
            var worldPos = _mapControl.Map.Navigator.Viewport.ScreenToWorld(point.X, point.Y);
            var lonLat = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);
            _coordsDisplay.Text = $"Lat: {lonLat.lat:F5}\u00B0 | Lon: {lonLat.lon:F5}\u00B0";
        }
        catch { }
    }

    #endregion

    #region Fast Track Update

    private void UpdateTrackFast()
    {
        if (_isDisposed || _trackLayer == null || TrackPoints == null) return;

        try
        {
            // Clear ALL layers and data for fresh start (prevents stale data from previous logs)
            SafeClearLayer(_trackLayer);
            SafeClearLayer(_crashLayer);
            SafeClearLayer(_markerLayer);
            SafeClearLayer(_eventLayer); // Clear events too
            SafeClearLayer(_currentPositionLayer); // Clear current position
            
            // Clear internal data structures
            _crashSegments.Clear();
            
            // Hide stats and crash alert from previous file
            HideStatsPanel();
            if (_crashAlert != null) _crashAlert.IsVisible = false;

            // Get and store track points
            _trackPoints = TrackPoints.Take(MaxTrackPointsStored).ToList();

            // Filter valid points
            var validPoints = _trackPoints
                .Where(p => IsValidCoordinate(p.Latitude, p.Longitude))
                .ToList();

            System.Diagnostics.Debug.WriteLine($"UpdateTrackFast: {validPoints.Count} valid points");

            if (validPoints.Count == 0)
            {
                HideStatsPanel();
                if (_legendPanel != null) _legendPanel.IsVisible = false;
                return;
            }

            // Downsample for rendering if needed
            var renderPoints = validPoints.Count > MaxTrackPointsForRender
                ? DownsamplePoints(validPoints, MaxTrackPointsForRender)
                : validPoints;

            // Build track line
            var coordinates = renderPoints
                .Select(p => {
                    var m = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
                    return new Coordinate(m.x, m.y);
                })
                .ToArray();

            if (coordinates.Length >= 2)
            {
                var line = new LineString(coordinates);
                var feature = new GeometryFeature { Geometry = line };
                feature.Styles.Add(new VectorStyle
                {
                    Line = new MapsuiPen(new MapsuiColor(59, 130, 246, 255), 3)
                });
                _trackLayer.Add(feature);
            }

            // Add start/end markers
            AddMarkerFast(validPoints.First(), new MapsuiColor(34, 197, 94, 255), "START");
            AddMarkerFast(validPoints.Last(), new MapsuiColor(239, 68, 68, 255), "END");

            // Quick crash detection on sampled points
            DetectCrashSegmentsFast(renderPoints);
            foreach (var seg in _crashSegments.Take(MaxCrashSegments))
                AddCrashSegmentFast(seg.Start, seg.End);

            // Show crash alert if needed
            if (_crashSegments.Count > 0 && _crashAlert != null)
            {
                _crashAlert.IsVisible = true;
                Task.Delay(3000).ContinueWith(_ => 
                    Dispatcher.UIThread.Post(() => { if (_crashAlert != null) _crashAlert.IsVisible = false; }));
            }

            // Update stats (fast)
            UpdateStatsFast(validPoints);

            // Show legend
            if (_legendPanel != null) _legendPanel.IsVisible = true;

            // Zoom to track
            ZoomToTrackFast(validPoints);
            
            _mapControl.InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in UpdateTrackFast: {ex.Message}");
            HideStatsPanel();
        }
    }

    private List<GpsTrackPoint> DownsamplePoints(List<GpsTrackPoint> points, int targetCount)
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

    private static bool IsValidCoordinate(double lat, double lon)
    {
        return Math.Abs(lat) > 0.001 && Math.Abs(lon) > 0.001 &&
               Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
    }

    private void SafeClearLayer(WritableLayer? layer)
    {
        if (layer == null || _isDisposed) return;
        try { layer.Clear(); } catch { }
    }

    private void DetectCrashSegmentsFast(List<GpsTrackPoint> points)
    {
        // Quick crash detection - only check significant changes
        for (int i = 1; i < points.Count && _crashSegments.Count < MaxCrashSegments; i++)
        {
            var prev = points[i - 1];
            var curr = points[i];
            var timeDiff = Math.Max(curr.Timestamp - prev.Timestamp, 0.1);
            var descentRate = (curr.Altitude - prev.Altitude) / timeDiff;

            // Simple crash detection: rapid descent or ground impact
            if (descentRate < -10 || (curr.Altitude <= 2 && prev.Altitude > 10))
                _crashSegments.Add((prev, curr));
        }
    }

    private void AddCrashSegmentFast(GpsTrackPoint start, GpsTrackPoint end)
    {
        if (_crashLayer == null) return;
        
        var s = SphericalMercator.FromLonLat(start.Longitude, start.Latitude);
        var e = SphericalMercator.FromLonLat(end.Longitude, end.Latitude);
        
        var line = new LineString(new[] { new Coordinate(s.x, s.y), new Coordinate(e.x, e.y) });
        var feature = new GeometryFeature { Geometry = line };
        feature.Styles.Add(new VectorStyle { Line = new MapsuiPen(new MapsuiColor(220, 38, 38, 255), 5) });
        _crashLayer.Add(feature);
    }

    private void AddMarkerFast(GpsTrackPoint point, MapsuiColor color, string label)
    {
        if (_markerLayer == null) return;

        var m = SphericalMercator.FromLonLat(point.Longitude, point.Latitude);
        var marker = new GeometryFeature { Geometry = new GeoPoint(m.x, m.y) };

        marker.Styles.Add(new SymbolStyle
        {
            Fill = new MapsuiBrush(color),
            Outline = new MapsuiPen(MapsuiColor.White, 2),
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 1.0
        });

        marker.Styles.Add(new LabelStyle
        {
            Text = label,
            BackColor = new MapsuiBrush(new MapsuiColor(0, 0, 0, 180)),
            ForeColor = MapsuiColor.White,
            Offset = new Offset(0, 20),
            Font = new Font { FontFamily = "Arial", Size = 9 }
        });

        _markerLayer.Add(marker);
    }

    private void UpdateStatsFast(List<GpsTrackPoint> points)
    {
        if (_statsPanel == null || points.Count < 2)
        {
            HideStatsPanel();
            return;
        }

        try
        {
            // Calculate basic stats
            double totalDistance = 0;
            for (int i = 1; i < Math.Min(points.Count, 1000); i++) // Limit for speed
            {
                totalDistance += HaversineDistance(
                    points[i - 1].Latitude, points[i - 1].Longitude,
                    points[i].Latitude, points[i].Longitude);
            }
            if (points.Count > 1000)
                totalDistance *= (double)points.Count / 1000; // Extrapolate

            var duration = points.Last().Timestamp - points.First().Timestamp;
            var altitudes = points.Select(p => p.Altitude).Where(a => a > 0).ToList();
            var speeds = points.Select(p => p.Speed).Where(s => s >= 0).ToList();

            _statsPanel.Text = $@"Flight Stats
\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
Dist:  {totalDistance:F1} km
Time:  {(int)(duration/60)}m {(int)(duration%60)}s
Alt:   {(altitudes.Any() ? altitudes.Max() : 0):F0}m max
Speed: {(speeds.Any() ? speeds.Max() : 0):F0} m/s
Points: {points.Count:N0}";

            if (_statsPanel.Parent is Border border)
                border.IsVisible = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating stats: {ex.Message}");
        }
    }

    private void HideStatsPanel()
    {
        if (_statsPanel?.Parent is Border border) border.IsVisible = false;
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private void ZoomToTrackFast(List<GpsTrackPoint> validPoints)
    {
        if (validPoints.Count == 0) return;

        try
        {
            var lats = validPoints.Select(p => p.Latitude).ToList();
            var lngs = validPoints.Select(p => p.Longitude).ToList();

            if (lats.Count == 1)
            {
                var m = SphericalMercator.FromLonLat(lngs[0], lats[0]);
                _map.Navigator?.CenterOn(m.x, m.y);
                _map.Navigator?.ZoomTo(15);
                return;
            }

            var min = SphericalMercator.FromLonLat(lngs.Min(), lats.Min());
            var max = SphericalMercator.FromLonLat(lngs.Max(), lats.Max());

            var w = Math.Max(max.x - min.x, 500);
            var h = Math.Max(max.y - min.y, 500);
            var padding = 0.15;

            var extent = new MRect(
                min.x - w * padding, min.y - h * padding,
                max.x + w * padding, max.y + h * padding);

            _map.Navigator?.ZoomToBox(extent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error zooming: {ex.Message}");
        }
    }

    #endregion

    #region Fast Events Update

    private void UpdateEventsFast()
    {
        if (_isDisposed || _eventLayer == null) return;

        try
        {
            SafeClearLayer(_eventLayer);

            var events = CriticalEvents?.Where(e => e.HasLocation && 
                IsValidCoordinate(e.Latitude!.Value, e.Longitude!.Value))
                .Take(MaxEvents).ToList();

            if (events == null || events.Count == 0) return;

            System.Diagnostics.Debug.WriteLine($"UpdateEventsFast: Adding {events.Count} critical events to map");

            foreach (var evt in events)
            {
                var m = SphericalMercator.FromLonLat(evt.Longitude!.Value, evt.Latitude!.Value);
                var marker = new GeometryFeature { Geometry = new GeoPoint(m.x, m.y) };

                // Color coding by severity
                var color = evt.Severity switch
                {
                    LogEventSeverity.Emergency or LogEventSeverity.Critical => new MapsuiColor(220, 38, 38, 255), // Red
                    LogEventSeverity.Error => new MapsuiColor(239, 68, 68, 255), // Light red
                    LogEventSeverity.Warning => new MapsuiColor(245, 158, 11, 255), // Amber
                    _ => new MapsuiColor(59, 130, 246, 255) // Blue
                };

                // Triangle marker for events
                marker.Styles.Add(new SymbolStyle
                {
                    Fill = new MapsuiBrush(color),
                    Outline = new MapsuiPen(MapsuiColor.White, 2),
                    SymbolType = SymbolType.Triangle,
                    SymbolScale = 1.2
                });

                // Add label with event title
                var label = evt.Title.Length > 12 ? evt.Title.Substring(0, 12) + "..." : evt.Title;
                marker.Styles.Add(new LabelStyle
                {
                    Text = label,
                    BackColor = new MapsuiBrush(new MapsuiColor(0, 0, 0, 220)),
                    ForeColor = MapsuiColor.White,
                    Offset = new Offset(0, -22),
                    Font = new Font { FontFamily = "Arial", Size = 8, Bold = true }
                });

                _eventLayer.Add(marker);
            }

            _mapControl.InvalidateVisual();
            System.Diagnostics.Debug.WriteLine($"UpdateEventsFast: Successfully rendered {events.Count} events");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating events: {ex.Message}");
        }
    }

    #endregion

    #region Fast Waypoints Update

    private void UpdateWaypointsFast()
    {
        if (_isDisposed || _waypointLayer == null || Waypoints == null) return;

        try
        {
            SafeClearLayer(_waypointLayer);

            _waypoints = Waypoints.Take(MaxWaypoints).ToList();
            var valid = _waypoints.Where(w => IsValidCoordinate(w.Latitude, w.Longitude)).ToList();

            if (valid.Count == 0) return;

            // Draw path
            if (valid.Count >= 2)
            {
                var coords = valid.Select(w => {
                    var m = SphericalMercator.FromLonLat(w.Longitude, w.Latitude);
                    return new Coordinate(m.x, m.y);
                }).ToArray();

                var path = new GeometryFeature { Geometry = new LineString(coords) };
                path.Styles.Add(new VectorStyle
                {
                    Line = new MapsuiPen(new MapsuiColor(255, 165, 0, 200), 2) { PenStyle = PenStyle.Dash }
                });
                _waypointLayer.Add(path);
            }

            // Add markers
            int idx = 1;
            foreach (var wp in valid)
            {
                var m = SphericalMercator.FromLonLat(wp.Longitude, wp.Latitude);
                var marker = new GeometryFeature { Geometry = new GeoPoint(m.x, m.y) };

                marker.Styles.Add(new SymbolStyle
                {
                    Fill = new MapsuiBrush(new MapsuiColor(255, 165, 0, 255)),
                    Outline = new MapsuiPen(MapsuiColor.White, 2),
                    SymbolType = SymbolType.Ellipse,
                    SymbolScale = 0.9
                });

                var label = wp.Label.StartsWith("WP") ? wp.Label : $"#{idx}";
                marker.Styles.Add(new LabelStyle
                {
                    Text = label,
                    BackColor = new MapsuiBrush(new MapsuiColor(0, 0, 0, 200)),
                    ForeColor = MapsuiColor.White,
                    Offset = new Offset(0, -18),
                    Font = new Font { FontFamily = "Arial", Size = 9 }
                });

                _waypointLayer.Add(marker);
                idx++;
            }

            _mapControl.InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating waypoints: {ex.Message}");
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Zoom to fit all track points and waypoints in view.
    /// </summary>
    public void ZoomToTrack()
    {
        if (_isDisposed) return;
        
        var validPoints = _trackPoints.Where(p => IsValidCoordinate(p.Latitude, p.Longitude)).ToList();
        var waypointPts = _waypoints.Where(w => IsValidCoordinate(w.Latitude, w.Longitude)).ToList();
        
        var allLats = validPoints.Select(p => p.Latitude).Concat(waypointPts.Select(w => w.Latitude)).ToList();
        var allLngs = validPoints.Select(p => p.Longitude).Concat(waypointPts.Select(w => w.Longitude)).ToList();

        if (allLats.Count == 0) return;

        if (allLats.Count == 1)
        {
            var m = SphericalMercator.FromLonLat(allLngs[0], allLats[0]);
            _map.Navigator?.CenterOn(m.x, m.y);
            _map.Navigator?.ZoomTo(15);
        }
        else
        {
            var min = SphericalMercator.FromLonLat(allLngs.Min(), allLats.Min());
            var max = SphericalMercator.FromLonLat(allLngs.Max(), allLats.Max());
            var w = Math.Max(max.x - min.x, 500);
            var h = Math.Max(max.y - min.y, 500);
            _map.Navigator?.ZoomToBox(new MRect(min.x - w * 0.15, min.y - h * 0.15, max.x + w * 0.15, max.y + h * 0.15));
        }
        
        _mapControl.InvalidateVisual();
    }

    /// <summary>
    /// Clear all track data from the map (called when loading a new log).
    /// Production-ready: Clears all layers to prevent stale data display.
    /// </summary>
    public void ClearTrack()
    {
        if (_isDisposed) return;
        
        _trackPoints.Clear();
        _crashSegments.Clear();
        _waypoints.Clear();
        
        SafeClearLayer(_trackLayer);
        SafeClearLayer(_crashLayer);
        SafeClearLayer(_markerLayer);
        SafeClearLayer(_eventLayer);
        SafeClearLayer(_currentPositionLayer);
        SafeClearLayer(_waypointLayer);
        
        HideStatsPanel();
        if (_legendPanel != null) _legendPanel.IsVisible = false;
        if (_crashAlert != null) _crashAlert.IsVisible = false;
        
        _mapControl.InvalidateVisual();
        
        System.Diagnostics.Debug.WriteLine("LogMapControl: Cleared all track data");
    }

    #endregion

    #region Event Handlers

    private void OnTrackPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isDisposed) ScheduleRefreshSafe(() => UpdateTrackFast());
    }
    
    private void OnCriticalEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isDisposed) ScheduleRefreshSafe(() => UpdateEventsFast());
    }

    #endregion

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_isDisposed) return;
        _isDisposed = true;

        // Clean up CTS safely
        CancellationTokenSource? ctsToDispose = null;
        lock (_refreshLock)
        {
            ctsToDispose = _refreshCts;
            _refreshCts = null;
        }

        try { ctsToDispose?.Cancel(); } catch { }
        try { ctsToDispose?.Dispose(); } catch { }

        try { _mapControl.PointerMoved -= OnMapPointerMoved; } catch { }

        SafeClearLayer(_trackLayer);
        SafeClearLayer(_crashLayer);
        SafeClearLayer(_markerLayer);
        SafeClearLayer(_eventLayer);
        SafeClearLayer(_currentPositionLayer);
        SafeClearLayer(_waypointLayer);

        _trackPoints.Clear();
        _crashSegments.Clear();
        _waypoints.Clear();

        try { (_map as IDisposable)?.Dispose(); } catch { }
    }
}

/// <summary>
/// GPS track point for map display with extended data
/// </summary>
public class GpsTrackPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public double Timestamp { get; set; }
    public double Speed { get; set; }
    public double Heading { get; set; }
    public int NumSatellites { get; set; }
    public double HorizontalAccuracy { get; set; }
    public double VerticalAccuracy { get; set; }
    public double GroundSpeed { get; set; }
    public double VerticalSpeed { get; set; }
}

/// <summary>
/// Waypoint point for map display with label
/// </summary>
public class WaypointPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Label { get; set; } = string.Empty;
}
