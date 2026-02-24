using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PavamanDroneConfigurator.Core.Models;
using ScottPlot;
using ScottPlot.Avalonia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PavamanDroneConfigurator.UI.Controls;

/// <summary>
/// Production-ready log graph control using ScottPlot for real data visualization.
/// Mission Planner-style dark theme with multi-series support and legend.
/// </summary>
public class LogGraphControl : UserControl
{
    private AvaPlot? _avaPlot;
    private LogGraphConfiguration? _configuration;

    /// <summary>
    /// Styled property for graph configuration data binding.
    /// </summary>
    public static readonly StyledProperty<LogGraphConfiguration?> GraphDataProperty =
        AvaloniaProperty.Register<LogGraphControl, LogGraphConfiguration?>(
            nameof(GraphData),
            defaultValue: null);

    /// <summary>
    /// Gets or sets the graph data configuration.
    /// </summary>
    public LogGraphConfiguration? GraphData
    {
        get => GetValue(GraphDataProperty);
        set => SetValue(GraphDataProperty, value);
    }

    public LogGraphControl()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == GraphDataProperty)
        {
            UpdateGraph(change.NewValue as LogGraphConfiguration);
        }
    }

    private void InitializeComponent()
    {
        _avaPlot = new AvaPlot
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };

        // Configure Mission Planner-style dark theme
        ConfigureMissionPlannerTheme();

        Content = _avaPlot;
    }

    /// <summary>
    /// Configure Mission Planner-style dark theme with enhanced visuals.
    /// </summary>
    private void ConfigureMissionPlannerTheme()
    {
        if (_avaPlot == null) return;

        // Mission Planner dark background (#1E1E1E)
        _avaPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E1E");
        _avaPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#252526");
        
        // Grid lines - subtle but visible
        _avaPlot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#3E3E42");
        _avaPlot.Plot.Grid.MinorLineColor = ScottPlot.Color.FromHex("#2D2D30");
        _avaPlot.Plot.Grid.MajorLineWidth = 1;
        _avaPlot.Plot.Grid.MinorLineWidth = 0.5f;
        
        // Axis styling - light gray for visibility
        _avaPlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#CCCCCC"));
        
        // Show legend with dark theme
        _avaPlot.Plot.ShowLegend();
        var legend = _avaPlot.Plot.Legend;
        legend.BackgroundColor = ScottPlot.Color.FromHex("#2D2D30").WithAlpha(0.9f);
        legend.OutlineColor = ScottPlot.Color.FromHex("#444444");
        legend.FontColor = ScottPlot.Color.FromHex("#E0E0E0");
        legend.FontSize = 11;
        legend.Location = Alignment.UpperRight;
    }

    /// <summary>
    /// Update the graph with new data configuration.
    /// </summary>
    public void UpdateGraph(LogGraphConfiguration? configuration)
    {
        if (_avaPlot == null) return;

        _configuration = configuration;

        // Clear existing plot
        _avaPlot.Plot.Clear();
        ConfigureMissionPlannerTheme();

        if (configuration == null || configuration.Series.Count == 0)
        {
            _avaPlot.Plot.Title("No Data - Select fields to graph");
            _avaPlot.Refresh();
            return;
        }

        // Track min/max for display and axis scaling
        double globalMin = double.MaxValue;
        double globalMax = double.MinValue;
        double globalXMin = double.MaxValue;
        double globalXMax = double.MinValue;
        int totalPoints = 0;
        int seriesAdded = 0;

        // Add each data series with Mission Planner styling
        int seriesIndex = 0;
        foreach (var series in configuration.Series.Where(s => s.IsVisible && s.Points.Count > 0))
        {
            var xs = series.Points.Select(p => p.X).ToArray();
            var ys = series.Points.Select(p => p.Y).ToArray();

            if (xs.Length == 0)
            {
                Debug.WriteLine($"LogGraphControl: Series '{series.Name}' has no X data points");
                continue;
            }

            // Log data stats for debugging
            var xMin = xs.Min();
            var xMax = xs.Max();
            var yMin = ys.Min();
            var yMax = ys.Max();
            var yAvg = ys.Average();
            
            Debug.WriteLine($"LogGraphControl: Series '{series.Name}' - Points:{xs.Length}, X:[{xMin:F2},{xMax:F2}], Y:[{yMin:F2},{yMax:F2}], Avg:{yAvg:F2}");

            // Track global ranges
            globalXMin = Math.Min(globalXMin, xMin);
            globalXMax = Math.Max(globalXMax, xMax);
            globalMin = Math.Min(globalMin, yMin);
            globalMax = Math.Max(globalMax, yMax);

            // Create scatter plot with signal-style rendering for performance
            var scatter = _avaPlot.Plot.Add.Scatter(xs, ys);
            scatter.LineWidth = GetLineWidth(seriesIndex);
            scatter.Color = ParseColor(series.Color);
            scatter.MarkerSize = 0; // Line only for performance with large datasets
            
            // Add min/max/avg info to legend
            scatter.LegendText = $"{series.Name} (min:{yMin:F2} max:{yMax:F2} avg:{yAvg:F2})";
            
            totalPoints += ys.Length;
            seriesIndex++;
            seriesAdded++;
        }

        if (seriesAdded == 0)
        {
            _avaPlot.Plot.Title("No Data - Selected fields have no data points");
            Debug.WriteLine("LogGraphControl: No series had valid data points");
            _avaPlot.Refresh();
            return;
        }

        // Handle edge case where all Y values are the same (constant line)
        // Add margin to make the line visible
        if (Math.Abs(globalMax - globalMin) < 0.001)
        {
            var margin = Math.Abs(globalMin) * 0.1;
            if (margin < 1.0) margin = 1.0; // Minimum margin of 1.0
            globalMin -= margin;
            globalMax += margin;
            Debug.WriteLine($"LogGraphControl: Applied Y-axis margin for constant values: [{globalMin:F2}, {globalMax:F2}]");
        }

        // Handle edge case where all X values are the same
        if (Math.Abs(globalXMax - globalXMin) < 0.001)
        {
            globalXMin -= 1.0;
            globalXMax += 1.0;
            Debug.WriteLine($"LogGraphControl: Applied X-axis margin for constant values: [{globalXMin:F2}, {globalXMax:F2}]");
        }

        // Configure axes with a small margin
        var yMargin = (globalMax - globalMin) * 0.05;
        var xMargin = (globalXMax - globalXMin) * 0.02;
        
        _avaPlot.Plot.Axes.SetLimits(
            globalXMin - xMargin, globalXMax + xMargin,
            globalMin - yMargin, globalMax + yMargin);
        
        // X-axis styling
        _avaPlot.Plot.XLabel("Time (sec)");
        
        // Y-axis styling - show "Value" or custom label
        var yLabel = configuration.YAxis.Label ?? "Value";
        _avaPlot.Plot.YLabel(yLabel);
        
        // Title with data info
        _avaPlot.Plot.Title($"Flight Data - {seriesAdded} series, {totalPoints:N0} points");
        
        Debug.WriteLine($"LogGraphControl: Rendered {seriesAdded} series with {totalPoints} total points, X:[{globalXMin:F2},{globalXMax:F2}], Y:[{globalMin:F2},{globalMax:F2}]");

        // Refresh the plot
        _avaPlot.Refresh();
    }

    /// <summary>
    /// Get line width based on series index for visual differentiation.
    /// </summary>
    private float GetLineWidth(int index)
    {
        // First series slightly thicker, others normal
        return index == 0 ? 2.0f : 1.5f;
    }

    /// <summary>
    /// Parse hex color string to ScottPlot Color.
    /// </summary>
    private ScottPlot.Color ParseColor(string hexColor)
    {
        try
        {
            if (string.IsNullOrEmpty(hexColor) || !hexColor.StartsWith("#"))
                return ScottPlot.Colors.Blue;

            return ScottPlot.Color.FromHex(hexColor);
        }
        catch
        {
            return ScottPlot.Colors.Blue;
        }
    }

    /// <summary>
    /// Export the current graph to PNG file.
    /// </summary>
    public void ExportToPng(string filePath, int width = 1920, int height = 1080)
    {
        if (_avaPlot == null || _configuration == null) return;

        _avaPlot.Plot.SavePng(filePath, width, height);
    }

    /// <summary>
    /// Reset zoom to auto-scale.
    /// </summary>
    public void ResetZoom()
    {
        if (_avaPlot == null) return;

        _avaPlot.Plot.Axes.AutoScale();
        _avaPlot.Refresh();
    }

    /// <summary>
    /// Pan the graph left
    /// </summary>
    public void PanLeft()
    {
        if (_avaPlot == null) return;
        
        var limits = _avaPlot.Plot.Axes.GetLimits();
        var panAmount = (limits.Right - limits.Left) * 0.1;
        _avaPlot.Plot.Axes.SetLimitsX(limits.Left - panAmount, limits.Right - panAmount);
        _avaPlot.Refresh();
    }

    /// <summary>
    /// Pan the graph right
    /// </summary>
    public void PanRight()
    {
        if (_avaPlot == null) return;
        
        var limits = _avaPlot.Plot.Axes.GetLimits();
        var panAmount = (limits.Right - limits.Left) * 0.1;
        _avaPlot.Plot.Axes.SetLimitsX(limits.Left + panAmount, limits.Right + panAmount);
        _avaPlot.Refresh();
    }

    /// <summary>
    /// Zoom in on the graph
    /// </summary>
    public void ZoomIn()
    {
        if (_avaPlot == null) return;
        
        var limits = _avaPlot.Plot.Axes.GetLimits();
        var centerX = (limits.Left + limits.Right) / 2;
        var centerY = (limits.Bottom + limits.Top) / 2;
        var rangeX = (limits.Right - limits.Left) * 0.4;
        var rangeY = (limits.Top - limits.Bottom) * 0.4;
        
        _avaPlot.Plot.Axes.SetLimits(
            centerX - rangeX, centerX + rangeX,
            centerY - rangeY, centerY + rangeY);
        _avaPlot.Refresh();
    }

    /// <summary>
    /// Zoom out on the graph
    /// </summary>
    public void ZoomOut()
    {
        if (_avaPlot == null) return;
        
        var limits = _avaPlot.Plot.Axes.GetLimits();
        var centerX = (limits.Left + limits.Right) / 2;
        var centerY = (limits.Bottom + limits.Top) / 2;
        var rangeX = (limits.Right - limits.Left) * 0.6;
        var rangeY = (limits.Top - limits.Bottom) * 0.6;
        
        _avaPlot.Plot.Axes.SetLimits(
            centerX - rangeX, centerX + rangeX,
            centerY - rangeY, centerY + rangeY);
        _avaPlot.Refresh();
    }

    /// <summary>
    /// Set the time range to display
    /// </summary>
    public void SetTimeRange(double startTime, double endTime)
    {
        if (_avaPlot == null) return;
        
        _avaPlot.Plot.Axes.SetLimitsX(startTime, endTime);
        _avaPlot.Refresh();
    }
}
