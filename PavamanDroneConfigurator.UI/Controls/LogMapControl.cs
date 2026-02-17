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
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AvaloniaColor = Avalonia.Media.Color;
using MapsuiColor = Mapsui.Styles.Color;
using MapsuiPen = Mapsui.Styles.Pen;
using MapsuiBrush = Mapsui.Styles.Brush;
using GeoPoint = NetTopologySuite.Geometries.Point;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.UI.Controls;

/// <summary>
/// Enhanced Map control for displaying GPS tracks from flight logs using OpenStreetMap tiles.
/// Features: GPS track visualization, crash detection (red lines), search bar, map controls,
/// altitude/speed data, event markers with color coding.
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
    
    // UI Controls
    private TextBox? _searchBox;
    private ListBox? _searchResults;
    private TextBlock? _statsPanel;
    private TextBlock? _coordsDisplay;
    private Border? _legendPanel;
    private Border? _crashAlert;
    
    // Data
    private List<GpsTrackPoint> _trackPoints = new();
    private List<(GpsTrackPoint Start, GpsTrackPoint End)> _crashSegments = new();
    
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

    public static readonly StyledProperty<bool> ShowSearchBarProperty =
        AvaloniaProperty.Register<LogMapControl, bool>(nameof(ShowSearchBar), true);

    public bool ShowSearchBar
    {
        get => GetValue(ShowSearchBarProperty);
        set => SetValue(ShowSearchBarProperty, value);
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

    #endregion

    public LogMapControl()
    {
        _map = new Map();
        _mapControl = new MapControl
        {
            Map = _map
        };

        // Build the control UI with search bar, stats, and legend
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

        // Search bar (top-left)
        var searchPanel = CreateSearchPanel();
        searchPanel.SetValue(Grid.RowProperty, 0);
        searchPanel.HorizontalAlignment = HorizontalAlignment.Left;
        searchPanel.VerticalAlignment = VerticalAlignment.Top;
        searchPanel.Margin = new Thickness(12);
        mainGrid.Children.Add(searchPanel);

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
                Text = "?? CRASH DETECTED - Emergency Event Recorded",
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

    private Border CreateSearchPanel()
    {
        var stackPanel = new StackPanel { Spacing = 0 };

        var searchGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        _searchBox = new TextBox
        {
            Watermark = "?? Search location...",
            Width = 280,
            FontSize = 13,
            Padding = new Thickness(12, 10)
        };
        _searchBox.KeyDown += OnSearchKeyDown;
        searchGrid.Children.Add(_searchBox);

        var searchBtn = new Button
        {
            Content = "Search",
            Padding = new Thickness(16, 10),
            Margin = new Thickness(4, 0, 0, 0)
        };
        searchBtn.Click += OnSearchClick;
        searchBtn.SetValue(Grid.ColumnProperty, 1);
        searchGrid.Children.Add(searchBtn);

        stackPanel.Children.Add(searchGrid);

        _searchResults = new ListBox
        {
            MaxHeight = 200,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _searchResults.SelectionChanged += OnSearchResultSelected;
        stackPanel.Children.Add(_searchResults);

        return new Border
        {
            Background = new SolidColorBrush(AvaloniaColor.FromArgb(245, 255, 255, 255)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 10, Color = AvaloniaColor.FromArgb(40, 0, 0, 0) }),
            Child = stackPanel
        };
    }

    private Border CreateStatsPanel()
    {
        _statsPanel = new TextBlock
        {
            Text = "Flight Statistics\n?????????????????\nLoad a log to see stats",
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
            ("?", "Zoom Out", () => _map.Navigator?.ZoomOut()),
            ("??", "Fit to Track", () => ZoomToTrack()),
            ("??", "Reset North", () => _map.Navigator?.RotateTo(0)),
            ("??", "Toggle Legend", () => { if (_legendPanel != null) _legendPanel.IsVisible = !_legendPanel.IsVisible; })
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
            ("#3B82F6", "Info Event")
        };

        foreach (var (color, label) in items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            row.Children.Add(new Border
            {
                Width = 16,
                Height = label.Contains("Path") || label.Contains("Crash") ? 4 : 12,
                CornerRadius = new CornerRadius(label.Contains("Path") || label.Contains("Crash") ? 2 : 6),
                Background = new SolidColorBrush(AvaloniaColor.Parse(color)),
                VerticalAlignment = VerticalAlignment.Center
            });
            
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
            if (control == this)
                Dispatcher.UIThread.Post(() => UpdateTrack());
        });

        CriticalEventsProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this)
                Dispatcher.UIThread.Post(() => UpdateCriticalEvents());
        });

        AllEventsProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this)
                Dispatcher.UIThread.Post(() => UpdateAllEvents());
        });

        CenterLatitudeProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this)
                Dispatcher.UIThread.Post(() => UpdateCenter());
        });

        CenterLongitudeProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this)
                Dispatcher.UIThread.Post(() => UpdateCenter());
        });

        CurrentPositionTimestampProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this)
                Dispatcher.UIThread.Post(() => UpdateCurrentPosition());
        });
    }

    private void InitializeMap()
    {
        try
        {
            // Add OpenStreetMap layer
            var osmLayer = OpenStreetMap.CreateTileLayer();
            osmLayer.Name = "OpenStreetMap";
            _map.Layers.Add(osmLayer);

            // Create layers in order (bottom to top)
            _trackLayer = new WritableLayer { Name = "GPS Track" };
            _crashLayer = new WritableLayer { Name = "Crash Segments" };
            _markerLayer = new WritableLayer { Name = "Start/End Markers" };
            _eventLayer = new WritableLayer { Name = "Events" };
            _currentPositionLayer = new WritableLayer { Name = "Current Position" };

            _map.Layers.Add(_trackLayer);
            _map.Layers.Add(_crashLayer);
            _map.Layers.Add(_markerLayer);
            _map.Layers.Add(_eventLayer);
            _map.Layers.Add(_currentPositionLayer);

            // Set initial view (world)
            _map.Navigator?.CenterOn(0, 0);
            _map.Navigator?.ZoomTo(2);

            _mapControl.InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing map: {ex.Message}");
        }
    }

    #region Search Functionality

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await PerformSearch();
        }
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        await PerformSearch();
    }

    private async Task PerformSearch()
    {
        if (_searchBox == null || string.IsNullOrWhiteSpace(_searchBox.Text))
            return;

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "PavamanDroneConfigurator/1.0");
            
            var query = Uri.EscapeDataString(_searchBox.Text);
            var response = await client.GetStringAsync(
                $"https://nominatim.openstreetmap.org/search?format=json&q={query}&limit=5");

            var results = JsonSerializer.Deserialize<List<NominatimResult>>(response);
            
            if (results != null && results.Count > 0 && _searchResults != null)
            {
                _searchResults.Items.Clear();
                foreach (var result in results)
                {
                    _searchResults.Items.Add(new ListBoxItem
                    {
                        Content = result.display_name,
                        Tag = result
                    });
                }
                _searchResults.IsVisible = true;
            }
            else if (_searchResults != null)
            {
                _searchResults.Items.Clear();
                _searchResults.Items.Add(new ListBoxItem { Content = "No results found" });
                _searchResults.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Search error: {ex.Message}");
        }
    }

    private void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_searchResults?.SelectedItem is ListBoxItem { Tag: NominatimResult result })
        {
            if (double.TryParse(result.lat, out var lat) && double.TryParse(result.lon, out var lon))
            {
                var mercator = SphericalMercator.FromLonLat(lon, lat);
                _map.Navigator?.CenterOn(mercator.x, mercator.y);
                _map.Navigator?.ZoomTo(15);
                _mapControl.InvalidateVisual();
            }
            _searchResults.IsVisible = false;
        }
    }

    private class NominatimResult
    {
        public string lat { get; set; } = "";
        public string lon { get; set; } = "";
        public string display_name { get; set; } = "";
    }

    #endregion

    #region Map Events

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_coordsDisplay == null || _map == null)
            return;

        try
        {
            var point = e.GetPosition(_mapControl);
            var worldPos = _mapControl.Map.Navigator.Viewport.ScreenToWorld(point.X, point.Y);
            var lonLat = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);
            
            _coordsDisplay.Text = $"Lat: {lonLat.lat:F6}° | Lon: {lonLat.lon:F6}°";
        }
        catch
        {
            // Ignore coordinate errors
        }
    }

    #endregion

    #region Track Update

    private void UpdateTrack()
    {
        if (_trackLayer == null || TrackPoints == null)
            return;

        try
        {
            // Clear existing layers
            _trackLayer.Clear();
            _crashLayer?.Clear();
            _markerLayer?.Clear();
            _crashSegments.Clear();

            _trackPoints = TrackPoints.ToList();
            if (_trackPoints.Count < 2)
            {
                HideStatsPanel();
                return;
            }

            // Filter valid points
            var validPoints = _trackPoints
                .Where(p => Math.Abs(p.Latitude) > 0.001 && Math.Abs(p.Longitude) > 0.001)
                .ToList();

            if (validPoints.Count < 2)
            {
                HideStatsPanel();
                return;
            }

            // Detect crash segments
            DetectCrashSegments(validPoints);

            // Create main track line (blue)
            var coordinates = validPoints
                .Select(p =>
                {
                    var mercator = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
                    return new Coordinate(mercator.x, mercator.y);
                })
                .ToArray();

            var lineString = new LineString(coordinates);
            var trackFeature = new GeometryFeature { Geometry = lineString };
            trackFeature.Styles.Add(new VectorStyle
            {
                Line = new MapsuiPen(new MapsuiColor(59, 130, 246, 255), 4) // Blue #3B82F6
            });
            _trackLayer.Add(trackFeature);

            // Add crash segments (red)
            foreach (var segment in _crashSegments)
            {
                AddCrashSegment(segment.Start, segment.End);
            }

            // Show crash alert if crashes detected
            if (_crashSegments.Count > 0 && _crashAlert != null)
            {
                _crashAlert.IsVisible = true;
                // Hide after 5 seconds
                Task.Delay(5000).ContinueWith(_ => 
                    Dispatcher.UIThread.Post(() => { if (_crashAlert != null) _crashAlert.IsVisible = false; }));
            }

            // Add start marker (green)
            AddMarker(validPoints.First(), new MapsuiColor(34, 197, 94, 255), "Start"); // #22C55E

            // Add end marker (red)
            AddMarker(validPoints.Last(), new MapsuiColor(239, 68, 68, 255), "End"); // #EF4444

            // Update statistics
            UpdateStatistics(validPoints);

            // Show legend
            if (_legendPanel != null)
                _legendPanel.IsVisible = true;

            // Zoom to track
            ZoomToTrack();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating track: {ex.Message}");
        }
    }

    private void DetectCrashSegments(List<GpsTrackPoint> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            var prev = points[i - 1];
            var curr = points[i];

            // Calculate descent rate
            var timeDiff = Math.Max(curr.Timestamp - prev.Timestamp, 0.1);
            var altDiff = curr.Altitude - prev.Altitude;
            var descentRate = altDiff / timeDiff;

            // Calculate speed change
            var speedDiff = Math.Abs(curr.Speed - prev.Speed);

            // Detect crash conditions:
            // 1. Rapid descent (> 10 m/s down)
            // 2. Sudden speed loss while low altitude
            // 3. Altitude drops to near zero from significant height
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
            Line = new MapsuiPen(new MapsuiColor(220, 38, 38, 255), 6) // Red #DC2626
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

        // Add label
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
            // Calculate distance (Haversine formula)
            double totalDistance = 0;
            for (int i = 1; i < points.Count; i++)
            {
                totalDistance += HaversineDistance(
                    points[i - 1].Latitude, points[i - 1].Longitude,
                    points[i].Latitude, points[i].Longitude);
            }

            // Duration
            var duration = points.Last().Timestamp - points.First().Timestamp;
            var minutes = (int)(duration / 60);
            var seconds = (int)(duration % 60);

            // Altitude stats
            var altitudes = points.Where(p => p.Altitude > 0).Select(p => p.Altitude).ToList();
            var maxAlt = altitudes.Count > 0 ? altitudes.Max() : 0;
            var minAlt = altitudes.Count > 0 ? altitudes.Min() : 0;
            var avgAlt = altitudes.Count > 0 ? altitudes.Average() : 0;

            // Speed stats
            var speeds = points.Where(p => p.Speed >= 0).Select(p => p.Speed).ToList();
            var maxSpeed = speeds.Count > 0 ? speeds.Max() : 0;
            var avgSpeed = speeds.Count > 0 ? speeds.Average() : 0;

            // Event count
            var eventCount = (CriticalEvents?.Count() ?? 0) + (AllEvents?.Count() ?? 0);

            _statsPanel.Text = $@"?? Flight Statistics
?????????????????????
?? Distance:    {totalDistance:F2} km
?? Duration:    {minutes}m {seconds}s
?????????????????????
?? Max Alt:     {maxAlt:F1} m
?? Min Alt:     {minAlt:F1} m
?? Avg Alt:     {avgAlt:F1} m
?????????????????????
?? Max Speed:   {maxSpeed:F1} m/s
?? Avg Speed:   {avgSpeed:F1} m/s
?????????????????????
?? GPS Points:  {points.Count:N0}
?? Events:      {eventCount}
?? Crashes:     {_crashSegments.Count}";

            // Show stats panel
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
        const double R = 6371; // Earth's radius in km
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
        if (_eventLayer == null || events == null)
            return;

        try
        {
            _eventLayer.Clear();

            foreach (var evt in events.Where(e => e.HasLocation))
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

                // Add label with event title
                eventMarker.Styles.Add(new LabelStyle
                {
                    Text = evt.Title,
                    BackColor = new MapsuiBrush(new MapsuiColor(0, 0, 0, 200)),
                    ForeColor = MapsuiColor.White,
                    Offset = new Offset(0, 22),
                    Font = new Font { FontFamily = "Arial", Size = 9 },
                    HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center
                });

                _eventLayer.Add(eventMarker);
            }

            _mapControl.InvalidateVisual();
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
                new MapsuiColor(127, 29, 29, 255),    // Dark red #7F1D1D
                SymbolType.Triangle,
                1.6
            ),
            LogEventSeverity.Critical => (
                new MapsuiColor(153, 27, 27, 255),    // #991B1B
                SymbolType.Rectangle,
                1.4
            ),
            LogEventSeverity.Error => (
                new MapsuiColor(220, 38, 38, 255),    // Red #DC2626
                SymbolType.Ellipse,
                1.2
            ),
            LogEventSeverity.Warning => (
                new MapsuiColor(245, 158, 11, 255),   // Amber #F59E0B
                SymbolType.Ellipse,
                1.0
            ),
            _ => (
                new MapsuiColor(59, 130, 246, 255),   // Blue #3B82F6
                SymbolType.Ellipse,
                0.9
            )
        };
    }

    #endregion

    #region Current Position

    private void UpdateCurrentPosition()
    {
        if (_currentPositionLayer == null || _trackPoints.Count == 0)
            return;

        try
        {
            _currentPositionLayer.Clear();

            // Find closest point to timestamp
            var closest = _trackPoints
                .OrderBy(p => Math.Abs(p.Timestamp - CurrentPositionTimestamp))
                .FirstOrDefault();

            if (closest == null || Math.Abs(closest.Latitude) < 0.001)
                return;

            var mercator = SphericalMercator.FromLonLat(closest.Longitude, closest.Latitude);
            var marker = new GeometryFeature
            {
                Geometry = new GeoPoint(mercator.x, mercator.y)
            };

            marker.Styles.Add(new SymbolStyle
            {
                Fill = new MapsuiBrush(new MapsuiColor(251, 191, 36, 255)), // Yellow #FBBF24
                Outline = new MapsuiPen(new MapsuiColor(255, 255, 255, 255), 3),
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 1.0
            });

            _currentPositionLayer.Add(marker);
            _mapControl.InvalidateVisual();
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
        if (_map == null)
            return;

        try
        {
            if (Math.Abs(CenterLatitude) < 0.001 && Math.Abs(CenterLongitude) < 0.001)
                return;

            var mercator = SphericalMercator.FromLonLat(CenterLongitude, CenterLatitude);
            _map.Navigator?.CenterOn(mercator.x, mercator.y);
            _mapControl.InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating center: {ex.Message}");
        }
    }

    /// <summary>
    /// Zoom to fit all GPS track points
    /// </summary>
    public void ZoomToTrack()
    {
        if (_trackPoints.Count < 2)
            return;

        try
        {
            var validPoints = _trackPoints
                .Where(p => Math.Abs(p.Latitude) > 0.001 && Math.Abs(p.Longitude) > 0.001)
                .ToList();

            if (validPoints.Count < 2)
                return;

            var minLat = validPoints.Min(p => p.Latitude);
            var maxLat = validPoints.Max(p => p.Latitude);
            var minLon = validPoints.Min(p => p.Longitude);
            var maxLon = validPoints.Max(p => p.Longitude);

            var min = SphericalMercator.FromLonLat(minLon, minLat);
            var max = SphericalMercator.FromLonLat(maxLon, maxLat);

            // Add padding
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
                _map.Navigator?.ZoomToBox(extent);
                _mapControl.InvalidateVisual();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error zooming to track: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear all data from the map
    /// </summary>
    public void ClearTrack()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _trackLayer?.Clear();
            _crashLayer?.Clear();
            _markerLayer?.Clear();
            _eventLayer?.Clear();
            _currentPositionLayer?.Clear();
            _trackPoints.Clear();
            _crashSegments.Clear();
            
            HideStatsPanel();
            if (_legendPanel != null) _legendPanel.IsVisible = false;
            if (_crashAlert != null) _crashAlert.IsVisible = false;
            
            _mapControl.InvalidateVisual();
        });
    }

    #endregion

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        try
        {
            _mapControl.PointerMoved -= OnMapPointerMoved;
            
            _trackLayer?.Clear();
            _crashLayer?.Clear();
            _markerLayer?.Clear();
            _eventLayer?.Clear();
            _currentPositionLayer?.Clear();

            // Remove layers from map
            foreach (var layer in new[] { _trackLayer, _crashLayer, _markerLayer, _eventLayer, _currentPositionLayer })
            {
                if (layer != null && _map?.Layers != null)
                    _map.Layers.Remove(layer);
            }

            (_map as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disposing map: {ex.Message}");
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
