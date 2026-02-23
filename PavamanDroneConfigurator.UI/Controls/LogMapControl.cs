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
/// OPTIMIZED: Thread-safe, memory-efficient, crash-resistant implementation.
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
    
    // Refresh management - OPTIMIZED
    private CancellationTokenSource? _refreshCts;
    private readonly object _refreshLock = new();
    private bool _isRefreshing;
    private bool _isDisposed;
    private DateTime _lastRefresh = DateTime.MinValue;
    private const int MinRefreshIntervalMs = 100;
    
    // Performance limits to prevent memory issues
    private const int MaxTrackPoints = 10000;
    private const int MaxEvents = 500;
    private const int MaxCrashSegments = 100;
    private const int MaxWaypoints = 500;
    
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
        _mapControl = new MapControl
        {
            Map = _map
        };

        // Build the control UI with stats and legend
        BuildControlUI();
        
        // Initialize map
        InitializeMap();
        
        // Setup property change handlers
        SetupPropertyHandlers();
    }

    private void BuildControlUI()
    {
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*"),
            ColumnDefinitions = new ColumnDefinitions("*")
        };

        // Map takes full space
        mainGrid.Children.Add(_mapControl);

        // Stats panel (top-right)
        var statsPanel = CreateStatsPanel();
        statsPanel.SetValue(Grid.RowProperty, 0);
        statsPanel.HorizontalAlignment = HorizontalAlignment.Right;
        statsPanel.VerticalAlignment = VerticalAlignment.Top;
        statsPanel.Margin = new Thickness(12);
        mainGrid.Children.Add(statsPanel);

        // Control buttons (right side)
        var controlsPanel = CreateControlsPanel();
        controlsPanel.SetValue(Grid.RowProperty, 0);
        controlsPanel.HorizontalAlignment = HorizontalAlignment.Right;
        controlsPanel.VerticalAlignment = VerticalAlignment.Center;
        controlsPanel.Margin = new Thickness(12);
        mainGrid.Children.Add(controlsPanel);

        // Legend (bottom-left)
        _legendPanel = CreateLegendPanel();
        _legendPanel.SetValue(Grid.RowProperty, 0);
        _legendPanel.HorizontalAlignment = HorizontalAlignment.Left;
        _legendPanel.VerticalAlignment = VerticalAlignment.Bottom;
        _legendPanel.Margin = new Thickness(12, 12, 12, 40);
        _legendPanel.IsVisible = false;
        mainGrid.Children.Add(_legendPanel);

        // Coordinates display (bottom-center)
        _coordsDisplay = new TextBlock
        {
            Text = "Move mouse over map to see coordinates",
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
                Text = "\u26A0 CRASH DETECTED - Emergency Event Recorded",
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                FontSize = 14
            }
        };
        mainGrid.Children.Add(_crashAlert);

        Content = mainGrid;

        // Subscribe to mouse move for coordinates
        _mapControl.PointerMoved += OnMapPointerMoved;
    }

    private Border CreateStatsPanel()
    {
        _statsPanel = new TextBlock
        {
            Text = "Flight Statistics\n\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\nLoad a log to see stats",
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            Foreground = new SolidColorBrush(AvaloniaColor.FromRgb(226, 232, 240))
        };

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaColor.FromArgb(240, 15, 23, 42)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            MinWidth = 220,
            Child = _statsPanel,
            IsVisible = false
        };
    }

    private StackPanel CreateControlsPanel()
    {
        var panel = new StackPanel
        {
            Spacing = 6
        };

        var buttons = new (string Content, string Tooltip, Action OnClick)[]
        {
            ("+", "Zoom In", () => _map.Navigator?.ZoomIn()),
            ("\u2212", "Zoom Out", () => _map.Navigator?.ZoomOut()),
            ("\u2302", "Fit to Track", () => ZoomToTrack()),
            ("\u21BB", "Reset North", () => _map.Navigator?.RotateTo(0)),
            ("\u25CF", "Toggle Legend", () => { if (_legendPanel != null) _legendPanel.IsVisible = !_legendPanel.IsVisible; })
        };

        foreach (var (content, tooltip, onClick) in buttons)
        {
            var btn = new Button
            {
                Content = content,
                Width = 36,
                Height = 36,
                FontSize = 16,
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
        var stack = new StackPanel { Spacing = 8 };

        stack.Children.Add(new TextBlock
        {
            Text = "Legend",
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var items = new (string Color, string Label)[]
        {
            ("#3B82F6", "Normal Flight Path"),
            ("#DC2626", "Crash / Emergency"),
            ("#22C55E", "Start Point"),
            ("#EF4444", "End Point"),
            ("#F59E0B", "Warning Event"),
            ("#991B1B", "Critical Event"),
            ("#3B82F6", "Info Event"),
            ("#FFA500", "Mission Waypoint"),
            ("#FFA500", "Mission Path (dashed)")
        };

        foreach (var (color, label) in items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            var height = label.Contains("Path") ? 4 : 12;
            var cornerRadius = label.Contains("Path") ? 2 : 6;
            
            var indicator = new Border
            {
                Width = 16,
                Height = height,
                CornerRadius = new CornerRadius(cornerRadius),
                Background = new SolidColorBrush(AvaloniaColor.Parse(color)),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Add dashed effect for mission path
            if (label.Contains("dashed"))
            {
                indicator.BorderBrush = new SolidColorBrush(AvaloniaColor.Parse(color));
                indicator.BorderThickness = new Thickness(2);
                indicator.Background = Brushes.Transparent;
            }
            
            row.Children.Add(indicator);
            
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            
            stack.Children.Add(row);
        }

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaColor.FromArgb(245, 255, 255, 255)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 10, Color = AvaloniaColor.FromArgb(40, 0, 0, 0) }),
            Child = stack
        };
    }

    private void SetupPropertyHandlers()
    {
        TrackPointsProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
            {
                // Subscribe to collection changes for ObservableCollection
                if (args.NewValue is INotifyCollectionChanged newCollection)
                {
                    newCollection.CollectionChanged += control.OnTrackPointsCollectionChanged;
                }
                control.ScheduleRefresh(() => control.UpdateTrack());
            }
        });

        CriticalEventsProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
            {
                // Subscribe to collection changes for ObservableCollection
                if (args.NewValue is INotifyCollectionChanged newCollection)
                {
                    newCollection.CollectionChanged += control.OnCriticalEventsCollectionChanged;
                }
                control.ScheduleRefresh(() => control.UpdateCriticalEvents());
            }
        });

        AllEventsProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
                control.ScheduleRefresh(() => control.UpdateAllEvents());
        });

        CurrentPositionTimestampProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
                control.ScheduleRefresh(() => control.UpdateCurrentPosition());
        });

        CenterLatitudeProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
                control.ScheduleRefresh(() => control.UpdateCenter());
        });

        CenterLongitudeProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
                control.ScheduleRefresh(() => control.UpdateCenter());
        });

        WaypointsProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this && !_isDisposed)
                control.ScheduleRefresh(() => control.UpdateWaypoints());
        });
    }

    private void ScheduleRefresh(Action refreshAction)
    {
        if (_isDisposed) return;

        lock (_refreshLock)
        {
            try
            {
                _refreshCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            
            try
            {
                _refreshCts?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
            
            _refreshCts = new CancellationTokenSource();
            var cts = _refreshCts;
            var timeSinceLastRefresh = (DateTime.Now - _lastRefresh).TotalMilliseconds;
            var delay = Math.Max(0, MinRefreshIntervalMs - (int)timeSinceLastRefresh);

            Task.Delay(delay, cts.Token).ContinueWith(_ =>
            {
                if (_isDisposed) return;
                
                try
                {
                    if (!cts.Token.IsCancellationRequested)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (_isDisposed) return;
                            
                            try
                            {
                                if (!cts.Token.IsCancellationRequested && !_isRefreshing)
                                {
                                    try
                                    {
                                        _isRefreshing = true;
                                        _lastRefresh = DateTime.Now;
                                        refreshAction();
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Map refresh error: {ex.Message}");
                                        System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                                    }
                                    finally
                                    {
                                        _isRefreshing = false;
                                    }
                                }
                            }
                            catch (ObjectDisposedException)
                            {
                            }
                        });
                    }
                }
                catch (ObjectDisposedException)
                {
                }
            }, TaskScheduler.Default);
        }
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
            _markerLayer = new WritableLayer { Name = "Start/End Markers" };
            _eventLayer = new WritableLayer { Name = "Events" };
            _currentPositionLayer = new WritableLayer { Name = "Current Position" };
            _waypointLayer = new WritableLayer { Name = "Waypoints" };

            _map.Layers.Add(_trackLayer);
            _map.Layers.Add(_crashLayer);
            _map.Layers.Add(_markerLayer);
            _map.Layers.Add(_eventLayer);
            _map.Layers.Add(_currentPositionLayer);
            _map.Layers.Add(_waypointLayer);

            _map.Navigator?.CenterOn(0, 0);
            _map.Navigator?.ZoomTo(2);

            _mapControl.InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing map: {ex.Message}");
        }
    }

    #region Map Events

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDisposed || _coordsDisplay == null || _map == null)
            return;

        try
        {
            var point = e.GetPosition(_mapControl);
            var worldPos = _mapControl.Map.Navigator.Viewport.ScreenToWorld(point.X, point.Y);
            var lonLat = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);

            if (!_isDisposed && _coordsDisplay != null)
                _coordsDisplay.Text = $"Lat: {lonLat.lat:F6}\u00B0 | Lon: {lonLat.lon:F6}\u00B0";
        }
        catch
        {
        }
    }

    #endregion

    #region Track Update

    private void UpdateTrack()
    {
        if (_isDisposed || _trackLayer == null || TrackPoints == null)
            return;

        try
        {
            lock (_dataLock)
            {
                SafeClearLayer(_trackLayer);
                SafeClearLayer(_crashLayer);
                SafeClearLayer(_markerLayer);
                _crashSegments.Clear();

                _trackPoints = TrackPoints
                    .Take(MaxTrackPoints)
                    .ToList();

                if (_trackPoints.Count < 2)
                {
                    HideStatsPanel();
                    return;
                }

                var validPoints = _trackPoints
                    .Where(p => IsValidCoordinate(p.Latitude, p.Longitude))
                    .ToList();

                if (validPoints.Count < 2)
                {
                    HideStatsPanel();
                    return;
                }

                DetectCrashSegments(validPoints);

                var coordinates = validPoints
                    .Select(p =>
                    {
                        try
                        {
                            var mercator = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
                            return new Coordinate(mercator.x, mercator.y);
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(c => c != null)
                    .Cast<Coordinate>()
                    .ToArray();

                if (coordinates.Length < 2)
                {
                    HideStatsPanel();
                    return;
                }

                var lineString = new LineString(coordinates);
                var trackFeature = new GeometryFeature { Geometry = lineString };
                trackFeature.Styles.Add(new VectorStyle
                {
                    Line = new MapsuiPen(new MapsuiColor(59, 130, 246, 255), 4)
                });
                _trackLayer.Add(trackFeature);

                foreach (var segment in _crashSegments.Take(MaxCrashSegments))
                {
                    AddCrashSegment(segment.Start, segment.End);
                }

                if (_crashSegments.Count > 0 && _crashAlert != null && !_isDisposed)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_isDisposed && _crashAlert != null)
                        {
                            _crashAlert.IsVisible = true;
                            Task.Delay(5000).ContinueWith(_ =>
                                Dispatcher.UIThread.Post(() =>
                                {
                                    if (!_isDisposed && _crashAlert != null)
                                        _crashAlert.IsVisible = false;
                                }));
                        }
                    });
                }

                AddMarker(validPoints.First(), new MapsuiColor(34, 197, 94, 255), "Start");
                AddMarker(validPoints.Last(), new MapsuiColor(239, 68, 68, 255), "End");

                UpdateStatistics(validPoints);

                if (_legendPanel != null && !_isDisposed)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_isDisposed && _legendPanel != null)
                            _legendPanel.IsVisible = true;
                    });
                }

                ZoomToTrack();
                SafeInvalidateMap();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating track: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            HideStatsPanel();
        }
    }

    private static bool IsValidCoordinate(double lat, double lon)
    {
        return Math.Abs(lat) > 0.001 &&
               Math.Abs(lon) > 0.001 &&
               Math.Abs(lat) <= 90 &&
               Math.Abs(lon) <= 180;
    }

    private void SafeClearLayer(WritableLayer? layer)
    {
        if (layer == null || _isDisposed) return;

        try
        {
            layer.Clear();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error clearing layer: {ex.Message}");
        }
    }

    private void SafeInvalidateMap()
    {
        if (_isDisposed) return;

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed)
                    _mapControl?.InvalidateVisual();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error invalidating map: {ex.Message}");
        }
    }

    private void DetectCrashSegments(List<GpsTrackPoint> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            var prev = points[i - 1];
            var curr = points[i];

            var timeDiff = Math.Max(curr.Timestamp - prev.Timestamp, 0.1);
            var altDiff = curr.Altitude - prev.Altitude;
            var descentRate = altDiff / timeDiff;

            var speedDiff = Math.Abs(curr.Speed - prev.Speed);

            bool isCrash = descentRate < -10 ||
                          (speedDiff > 15 && curr.Altitude < 50) ||
                          (curr.Altitude <= 2 && prev.Altitude > 15);

            if (isCrash)
            {
                _crashSegments.Add((prev, curr));
            }
        }
    }

    private void AddCrashSegment(GpsTrackPoint start, GpsTrackPoint end)
    {
        if (_crashLayer == null) return;

        var startMercator = SphericalMercator.FromLonLat(start.Longitude, start.Latitude);
        var endMercator = SphericalMercator.FromLonLat(end.Longitude, end.Latitude);

        var line = new LineString(new[]
        {
            new Coordinate(startMercator.x, startMercator.y),
            new Coordinate(endMercator.x, endMercator.y)
        });

        var feature = new GeometryFeature { Geometry = line };
        feature.Styles.Add(new VectorStyle
        {
            Line = new MapsuiPen(new MapsuiColor(220, 38, 38, 255), 6)
        });

        _crashLayer.Add(feature);
    }

    private void AddMarker(GpsTrackPoint point, MapsuiColor color, string label)
    {
        if (_markerLayer == null) return;

        var mercator = SphericalMercator.FromLonLat(point.Longitude, point.Latitude);
        var marker = new GeometryFeature
        {
            Geometry = new GeoPoint(mercator.x, mercator.y)
        };

        marker.Styles.Add(new SymbolStyle
        {
            Fill = new MapsuiBrush(color),
            Outline = new MapsuiPen(new MapsuiColor(255, 255, 255, 255), 3),
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 1.2
        });

        marker.Styles.Add(new LabelStyle
        {
            Text = label,
            BackColor = new MapsuiBrush(new MapsuiColor(0, 0, 0, 180)),
            ForeColor = MapsuiColor.White,
            Offset = new Offset(0, 25),
            Font = new Font { FontFamily = "Arial", Size = 10 },
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center
        });

        _markerLayer.Add(marker);
    }

    private void UpdateStatistics(List<GpsTrackPoint> points)
    {
        if (_statsPanel == null || points.Count < 2)
        {
            HideStatsPanel();
            return;
        }

        try
        {
            double totalDistance = 0;
            for (int i = 1; i < points.Count; i++)
            {
                totalDistance += HaversineDistance(
                    points[i - 1].Latitude, points[i - 1].Longitude,
                    points[i].Latitude, points[i].Longitude);
            }

            var duration = points.Last().Timestamp - points.First().Timestamp;
            var minutes = (int)(duration / 60);
            var seconds = (int)(duration % 60);

            var altitudes = points.Where(p => p.Altitude > 0).Select(p => p.Altitude).ToList();
            var maxAlt = altitudes.Count > 0 ? altitudes.Max() : 0;
            var minAlt = altitudes.Count > 0 ? altitudes.Min() : 0;
            var avgAlt = altitudes.Count > 0 ? altitudes.Average() : 0;

            var speeds = points.Where(p => p.Speed >= 0).Select(p => p.Speed).ToList();
            var maxSpeed = speeds.Count > 0 ? speeds.Max() : 0;
            var avgSpeed = speeds.Count > 0 ? speeds.Average() : 0;

            var eventCount = (CriticalEvents?.Count() ?? 0) + (AllEvents?.Count() ?? 0);

            _statsPanel.Text = $@"Flight Statistics
{new string('\u2500', 21)}
Distance:    {totalDistance:F2} km
Duration:    {minutes}m {seconds}s
{new string('\u2500', 21)}
Max Alt:     {maxAlt:F1} m
Min Alt:     {minAlt:F1} m
Avg Alt:     {avgAlt:F1} m
{new string('\u2500', 21)}
Max Speed:   {maxSpeed:F1} m/s
Avg Speed:   {avgSpeed:F1} m/s
{new string('\u2500', 21)}
GPS Points:  {points.Count:N0}
Events:      {eventCount}
Crashes:     {_crashSegments.Count}";

            if (_statsPanel.Parent is Border border)
                border.IsVisible = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating statistics: {ex.Message}");
        }
    }

    private void HideStatsPanel()
    {
        if (_statsPanel?.Parent is Border border)
            border.IsVisible = false;
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    #endregion

    #region Events Update

    private void UpdateCriticalEvents()
    {
        UpdateEvents(CriticalEvents);
    }

    private void UpdateAllEvents()
    {
        UpdateEvents(AllEvents ?? CriticalEvents);
    }

    private void UpdateEvents(IEnumerable<LogEvent>? events)
    {
        if (_isDisposed || _eventLayer == null || events == null)
            return;

        try
        {
            lock (_dataLock)
            {
                SafeClearLayer(_eventLayer);

                var limitedEvents = events
                    .Where(e => e.HasLocation && IsValidCoordinate(e.Latitude!.Value, e.Longitude!.Value))
                    .Take(MaxEvents)
                    .ToList();

                foreach (var evt in limitedEvents)
                {
                    try
                    {
                        var mercator = SphericalMercator.FromLonLat(evt.Longitude!.Value, evt.Latitude!.Value);
                        var eventMarker = new GeometryFeature
                        {
                            Geometry = new GeoPoint(mercator.x, mercator.y)
                        };

                        var (color, symbolType, scale) = GetEventMarkerStyle(evt);
                        eventMarker.Styles.Add(new SymbolStyle
                        {
                            Fill = new MapsuiBrush(color),
                            Outline = new MapsuiPen(new MapsuiColor(255, 255, 255, 255), 2),
                            SymbolType = symbolType,
                            SymbolScale = scale
                        });

                        var title = evt.Title.Length > 30 ? evt.Title.Substring(0, 27) + "..." : evt.Title;
                        eventMarker.Styles.Add(new LabelStyle
                        {
                            Text = title,
                            BackColor = new MapsuiBrush(new MapsuiColor(0, 0, 0, 200)),
                            ForeColor = MapsuiColor.White,
                            Offset = new Offset(0, 22),
                            Font = new Font { FontFamily = "Arial", Size = 9 },
                            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center
                        });

                        _eventLayer.Add(eventMarker);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error adding event marker: {ex.Message}");
                    }
                }

                SafeInvalidateMap();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating events: {ex.Message}");
        }
    }

    private static (MapsuiColor Color, SymbolType Symbol, double Scale) GetEventMarkerStyle(LogEvent evt)
    {
        return evt.Severity switch
        {
            LogEventSeverity.Emergency => (
                new MapsuiColor(127, 29, 29, 255),
                SymbolType.Triangle,
                1.6
            ),
            LogEventSeverity.Critical => (
                new MapsuiColor(153, 27, 27, 255),
                SymbolType.Rectangle,
                1.4
            ),
            LogEventSeverity.Error => (
                new MapsuiColor(220, 38, 38, 255),
                SymbolType.Ellipse,
                1.2
            ),
            LogEventSeverity.Warning => (
                new MapsuiColor(245, 158, 11, 255),
                SymbolType.Ellipse,
                1.0
            ),
            _ => (
                new MapsuiColor(59, 130, 246, 255),
                SymbolType.Ellipse,
                0.9
            )
        };
    }

    #endregion

    #region Current Position

    private void UpdateCurrentPosition()
    {
        if (_isDisposed || _currentPositionLayer == null || _trackPoints.Count == 0)
            return;

        try
        {
            lock (_dataLock)
            {
                SafeClearLayer(_currentPositionLayer);

                GpsTrackPoint? closest = null;
                double minDiff = double.MaxValue;

                foreach (var p in _trackPoints)
                {
                    var diff = Math.Abs(p.Timestamp - CurrentPositionTimestamp);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        closest = p;
                    }
                }

                if (closest == null || !IsValidCoordinate(closest.Latitude, closest.Longitude))
                    return;

                var mercator = SphericalMercator.FromLonLat(closest.Longitude, closest.Latitude);
                var marker = new GeometryFeature
                {
                    Geometry = new GeoPoint(mercator.x, mercator.y)
                };

                marker.Styles.Add(new SymbolStyle
                {
                    Fill = new MapsuiBrush(new MapsuiColor(251, 191, 36, 255)),
                    Outline = new MapsuiPen(new MapsuiColor(255, 255, 255, 255), 3),
                    SymbolType = SymbolType.Ellipse,
                    SymbolScale = 1.0
                });

                _currentPositionLayer.Add(marker);
                SafeInvalidateMap();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating current position: {ex.Message}");
        }
    }

    #endregion

    #region Center and Zoom

    private void UpdateCenter()
    {
        if (_isDisposed || _map == null)
            return;

        try
        {
            if (!IsValidCoordinate(CenterLatitude, CenterLongitude))
                return;

            var mercator = SphericalMercator.FromLonLat(CenterLongitude, CenterLatitude);

            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed && _map.Navigator != null)
                {
                    _map.Navigator.CenterOn(mercator.x, mercator.y);
                    SafeInvalidateMap();
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating center: {ex.Message}");
        }
    }

    public void ZoomToTrack()
    {
        if (_isDisposed || _trackPoints.Count < 2)
            return;

        try
        {
            lock (_dataLock)
            {
                var validPoints = _trackPoints
                    .Where(p => IsValidCoordinate(p.Latitude, p.Longitude))
                    .ToList();

                if (validPoints.Count < 2)
                    return;

                var minLat = validPoints.Min(p => p.Latitude);
                var maxLat = validPoints.Max(p => p.Latitude);
                var minLon = validPoints.Min(p => p.Longitude);
                var maxLon = validPoints.Max(p => p.Longitude);

                var min = SphericalMercator.FromLonLat(minLon, minLat);
                var max = SphericalMercator.FromLonLat(maxLon, maxLat);

                var padding = 0.1;
                var width = max.x - min.x;
                var height = max.y - min.y;

                var extent = new MRect(
                    min.x - width * padding,
                    min.y - height * padding,
                    max.x + width * padding,
                    max.y + height * padding);

                Dispatcher.UIThread.Post(() =>
                {
                    if (!_isDisposed && _map.Navigator != null)
                    {
                        _map.Navigator.ZoomToBox(extent);
                        SafeInvalidateMap();
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error zooming to track: {ex.Message}");
        }
    }

    public void ClearTrack()
    {
        lock (_dataLock)
        {
            _trackPoints.Clear();
            SafeClearLayer(_trackLayer);
            SafeInvalidateMap();
        }
    }
    
    private void OnTrackPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isDisposed)
        {
            ScheduleRefresh(() => UpdateTrack());
        }
    }
    
    private void OnCriticalEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isDisposed)
        {
            ScheduleRefresh(() => UpdateCriticalEvents());
        }
    }

    private void UpdateWaypoints()
    {
        if (_isDisposed || _waypointLayer == null || Waypoints == null)
            return;

        try
        {
            lock (_dataLock)
            {
                SafeClearLayer(_waypointLayer);

                _waypoints = Waypoints
                    .Take(MaxWaypoints)
                    .ToList();

                if (_waypoints.Count == 0)
                    return;

                if (_waypoints.Count >= 2)
                {
                    var pathCoordinates = _waypoints
                        .Where(wp => IsValidCoordinate(wp.Latitude, wp.Longitude))
                        .Select(wp =>
                        {
                            var mercator = SphericalMercator.FromLonLat(wp.Longitude, wp.Latitude);
                            return new Coordinate(mercator.x, mercator.y);
                        })
                        .ToArray();

                    if (pathCoordinates.Length >= 2)
                    {
                        var missionPath = new LineString(pathCoordinates);
                        var pathFeature = new GeometryFeature { Geometry = missionPath };
                        pathFeature.Styles.Add(new VectorStyle
                        {
                            Line = new MapsuiPen(new MapsuiColor(255, 165, 0, 200), 3)
                            {
                                PenStyle = PenStyle.Dash
                            }
                        });
                        _waypointLayer.Add(pathFeature);
                    }
                }

                int waypointIndex = 1;
                foreach (var waypoint in _waypoints)
                {
                    if (!IsValidCoordinate(waypoint.Latitude, waypoint.Longitude))
                        continue;

                    var mercator = SphericalMercator.FromLonLat(waypoint.Longitude, waypoint.Latitude);
                    var waypointMarker = new GeometryFeature
                    {
                        Geometry = new GeoPoint(mercator.x, mercator.y)
                    };

                    var (markerColor, markerScale) = GetWaypointStyle(waypoint.Label, waypointIndex);

                    waypointMarker.Styles.Add(new SymbolStyle
                    {
                        Fill = new MapsuiBrush(markerColor),
                        Outline = new MapsuiPen(MapsuiColor.White, 2),
                        SymbolType = SymbolType.Ellipse,
                        SymbolScale = markerScale
                    });

                    var displayLabel = GetWaypointDisplayLabel(waypoint.Label, waypointIndex);
                    waypointMarker.Styles.Add(new LabelStyle
                    {
                        Text = displayLabel,
                        BackColor = new MapsuiBrush(new MapsuiColor(0, 0, 0, 220)),
                        ForeColor = MapsuiColor.White,
                        Offset = new Offset(0, -24),
                        Font = new Font { FontFamily = "Arial", Size = 10 },
                        HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                        BorderColor = new MapsuiColor(255, 165, 0, 255)
                    });

                    _waypointLayer.Add(waypointMarker);
                    waypointIndex++;
                }

                if (_waypoints.Count > 0 && IsValidCoordinate(_waypoints[0].Latitude, _waypoints[0].Longitude))
                {
                    var homeWp = _waypoints[0];
                    var homeMercator = SphericalMercator.FromLonLat(homeWp.Longitude, homeWp.Latitude);
                    
                    var homeMarker = new GeometryFeature
                    {
                        Geometry = new GeoPoint(homeMercator.x, homeMercator.y)
                    };
                    
                    homeMarker.Styles.Add(new SymbolStyle
                    {
                        Fill = new MapsuiBrush(new MapsuiColor(34, 197, 94, 255)),
                        Outline = new MapsuiPen(MapsuiColor.White, 3),
                        SymbolType = SymbolType.Rectangle,
                        SymbolScale = 1.3
                    });
                    
                    homeMarker.Styles.Add(new LabelStyle
                    {
                        Text = "H",
                        ForeColor = MapsuiColor.White,
                        Font = new Font { FontFamily = "Arial", Size = 12 },
                        HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center
                    });
                    
                    _waypointLayer.Add(homeMarker);
                }

                SafeInvalidateMap();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating waypoints: {ex.Message}");
        }
    }

    private static (MapsuiColor Color, double Scale) GetWaypointStyle(string label, int index)
    {
        var labelUpper = label.ToUpperInvariant();
        
        if (labelUpper.Contains("TAKEOFF"))
            return (new MapsuiColor(34, 197, 94, 255), 1.2);
        
        if (labelUpper.Contains("LAND"))
            return (new MapsuiColor(239, 68, 68, 255), 1.2);
        
        if (labelUpper.Contains("RTL") || labelUpper.Contains("RETURN"))
            return (new MapsuiColor(139, 92, 246, 255), 1.2);
        
        if (labelUpper.Contains("LOITER") || labelUpper.Contains("HOLD"))
            return (new MapsuiColor(59, 130, 246, 255), 1.1);
        
        return (new MapsuiColor(255, 165, 0, 255), 1.0);
    }

    private static string GetWaypointDisplayLabel(string label, int index)
    {
        var labelUpper = label.ToUpperInvariant();
        
        if (labelUpper.Contains("TAKEOFF"))
            return "\u2191 Takeoff";
        
        if (labelUpper.Contains("LAND"))
            return "\u2193 Land";
        
        if (labelUpper.Contains("RTL") || labelUpper.Contains("RETURN"))
            return "\u21A9 RTL";
        
        if (label.StartsWith("WP"))
            return label;
        
        return $"#{index}";
    }

    #endregion

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            lock (_refreshLock)
            {
                try
                {
                    _refreshCts?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                
                try
                {
                    _refreshCts?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
                
                _refreshCts = null;
            }

            try
            {
                if (_mapControl != null)
                    _mapControl.PointerMoved -= OnMapPointerMoved;
            }
            catch { }

            lock (_dataLock)
            {
                SafeClearLayer(_trackLayer);
                SafeClearLayer(_crashLayer);
                SafeClearLayer(_markerLayer);
                SafeClearLayer(_eventLayer);
                SafeClearLayer(_currentPositionLayer);
                SafeClearLayer(_waypointLayer);

                if (_map?.Layers != null)
                {
                    var layersToRemove = new ILayer?[] { _trackLayer, _crashLayer, _markerLayer, _eventLayer, _currentPositionLayer, _waypointLayer, _osmLayer };
                    foreach (var layer in layersToRemove)
                    {
                        if (layer != null)
                        {
                            try
                            {
                                _map.Layers.Remove(layer);
                            }
                            catch { }
                        }
                    }
                }

                _trackPoints.Clear();
                _crashSegments.Clear();
                _waypoints.Clear();
            }

            try
            {
                (_map as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing map: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnUnloaded: {ex.Message}");
        }
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
