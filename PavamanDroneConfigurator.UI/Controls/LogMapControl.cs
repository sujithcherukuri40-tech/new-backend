using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
using Mapsui.UI.Avalonia;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaColor = Avalonia.Media.Color;
using MapsuiColor = Mapsui.Styles.Color;
using MapsuiPen = Mapsui.Styles.Pen;
using MapsuiBrush = Mapsui.Styles.Brush;
using GeoPoint = NetTopologySuite.Geometries.Point;

namespace PavamanDroneConfigurator.UI.Controls;

/// <summary>
/// Map control for displaying GPS tracks from flight logs using OpenStreetMap tiles.
/// </summary>
public class LogMapControl : UserControl
{
    private readonly MapControl _mapControl;
    private readonly Map _map;
    private GeometryFeature? _trackFeature;
    private WritableLayer? _trackLayer;
    private WritableLayer? _markerLayer;
    
    // Property for GPS track data
    public static readonly StyledProperty<IEnumerable<GpsTrackPoint>?> TrackPointsProperty =
        AvaloniaProperty.Register<LogMapControl, IEnumerable<GpsTrackPoint>?>(nameof(TrackPoints));

    public IEnumerable<GpsTrackPoint>? TrackPoints
    {
        get => GetValue(TrackPointsProperty);
        set => SetValue(TrackPointsProperty, value);
    }

    // Property for center latitude
    public static readonly StyledProperty<double> CenterLatitudeProperty =
        AvaloniaProperty.Register<LogMapControl, double>(nameof(CenterLatitude));

    public double CenterLatitude
    {
        get => GetValue(CenterLatitudeProperty);
        set => SetValue(CenterLatitudeProperty, value);
    }

    // Property for center longitude
    public static readonly StyledProperty<double> CenterLongitudeProperty =
        AvaloniaProperty.Register<LogMapControl, double>(nameof(CenterLongitude));

    public double CenterLongitude
    {
        get => GetValue(CenterLongitudeProperty);
        set => SetValue(CenterLongitudeProperty, value);
    }

    public LogMapControl()
    {
        _map = new Map();
        _mapControl = new MapControl
        {
            Map = _map
        };

        Content = _mapControl;
        
        // Initialize map with OSM tiles
        InitializeMap();

        // Listen for property changes
        TrackPointsProperty.Changed.AddClassHandler<LogMapControl>((control, args) =>
        {
            if (control == this)
                Dispatcher.UIThread.Post(() => UpdateTrack());
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
    }

    private void InitializeMap()
    {
        try
        {
            // Add OpenStreetMap layer
            var osmLayer = OpenStreetMap.CreateTileLayer();
            osmLayer.Name = "OpenStreetMap";
            _map.Layers.Add(osmLayer);

            // Create track layer
            _trackLayer = new WritableLayer
            {
                Name = "GPS Track",
                Style = null // Will be set per feature
            };
            _map.Layers.Add(_trackLayer);

            // Create marker layer for events/waypoints
            _markerLayer = new WritableLayer
            {
                Name = "Markers",
                Style = null
            };
            _map.Layers.Add(_markerLayer);

            // Set initial center (default to world view)
            _map.Navigator?.CenterOn(0, 0);
            _map.Navigator?.ZoomTo(2);

            _mapControl.InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing map: {ex.Message}");
        }
    }

    private void UpdateTrack()
    {
        if (_trackLayer == null || TrackPoints == null)
            return;

        try
        {
            // Clear existing track
            _trackLayer.Clear();
            _trackFeature = null;

            var points = TrackPoints.ToList();
            if (points.Count < 2)
                return;

            // Create line geometry from GPS points
            var coordinates = points
                .Where(p => Math.Abs(p.Latitude) > 0.001 || Math.Abs(p.Longitude) > 0.001) // Filter invalid points
                .Select(p =>
                {
                    // Convert WGS84 (lat/lon) to Spherical Mercator (Web Mercator)
                    var mercator = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
                    return new Coordinate(mercator.x, mercator.y);
                })
                .ToArray();

            if (coordinates.Length < 2)
                return;

            // Create line string
            var lineString = new LineString(coordinates);

            // Create feature with styling
            _trackFeature = new GeometryFeature
            {
                Geometry = lineString
            };

            // Style the track line
            _trackFeature.Styles.Add(new VectorStyle
            {
                Line = new MapsuiPen(new MapsuiColor(0, 122, 255, 255), 3) // Blue track line
            });

            // Add start marker (green)
            var startPoint = points.First();
            var startMercator = SphericalMercator.FromLonLat(startPoint.Longitude, startPoint.Latitude);
            var startMarker = new GeometryFeature
            {
                Geometry = new GeoPoint(startMercator.x, startMercator.y)
            };
            startMarker.Styles.Add(new SymbolStyle
            {
                Fill = new MapsuiBrush(new MapsuiColor(16, 185, 129, 255)), // Green
                Outline = new MapsuiPen(new MapsuiColor(255, 255, 255, 255), 2),
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 1.0
            });

            // Add end marker (red)
            var endPoint = points.Last();
            var endMercator = SphericalMercator.FromLonLat(endPoint.Longitude, endPoint.Latitude);
            var endMarker = new GeometryFeature
            {
                Geometry = new GeoPoint(endMercator.x, endMercator.y)
            };
            endMarker.Styles.Add(new SymbolStyle
            {
                Fill = new MapsuiBrush(new MapsuiColor(239, 68, 68, 255)), // Red
                Outline = new MapsuiPen(new MapsuiColor(255, 255, 255, 255), 2),
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 1.0
            });

            _trackLayer.Add(_trackFeature);
            _markerLayer?.Add(startMarker);
            _markerLayer?.Add(endMarker);

            // Zoom to track extent
            var extent = lineString.EnvelopeInternal;
            if (extent.Width > 0 && extent.Height > 0)
            {
                // Add padding (10%)
                var padding = 0.1;
                var paddedExtent = new MRect(
                    extent.MinX - extent.Width * padding,
                    extent.MinY - extent.Height * padding,
                    extent.MaxX + extent.Width * padding,
                    extent.MaxY + extent.Height * padding
                );

                Dispatcher.UIThread.Post(() =>
                {
                    _map.Navigator?.ZoomToBox(paddedExtent);
                    _mapControl.InvalidateVisual();
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating track: {ex.Message}");
        }
    }

    private void UpdateCenter()
    {
        if (_map == null)
            return;

        try
        {
            if (Math.Abs(CenterLatitude) < 0.001 && Math.Abs(CenterLongitude) < 0.001)
                return;

            // Convert to Web Mercator
            var mercator = SphericalMercator.FromLonLat(CenterLongitude, CenterLatitude);

            Dispatcher.UIThread.Post(() =>
            {
                _map.Navigator?.CenterOn(mercator.x, mercator.y);
                _mapControl.InvalidateVisual();
            });
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
        Dispatcher.UIThread.Post(() => UpdateTrack());
    }

    /// <summary>
    /// Clear the track from the map
    /// </summary>
    public void ClearTrack()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _trackLayer?.Clear();
            _markerLayer?.Clear();
            _mapControl.InvalidateVisual();
        });
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        
        try
        {
            // Clear layers first
            _trackLayer?.Clear();
            _markerLayer?.Clear();
            
            // Remove from map
            if (_trackLayer != null && _map?.Layers != null)
            {
                _map.Layers.Remove(_trackLayer);
            }
            if (_markerLayer != null && _map?.Layers != null)
            {
                _map.Layers.Remove(_markerLayer);
            }
            
            // Dispose resources
            (_trackLayer as IDisposable)?.Dispose();
            (_markerLayer as IDisposable)?.Dispose();
            (_trackFeature as IDisposable)?.Dispose();
            (_map as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disposing map: {ex.Message}");
        }
    }
}

/// <summary>
/// GPS track point for map display
/// </summary>
public class GpsTrackPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public double Timestamp { get; set; }
}
