using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PavamanDroneConfigurator.Core.Models;
using ScottPlot;
using ScottPlot.Avalonia;
using System;
using System.Collections.Generic;
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

        // Track min/max for display
        double globalMin = double.MaxValue;
        double globalMax = double.MinValue;
        int totalPoints = 0;

        // Add each data series with Mission Planner styling
        int seriesIndex = 0;
        foreach (var series in configuration.Series.Where(s => s.IsVisible && s.Points.Count > 0))
        {
            var xs = series.Points.Select(p => p.X).ToArray();
            var ys = series.Points.Select(p => p.Y).ToArray();

            if (xs.Length == 0) continue;

            // Create scatter plot with signal-style rendering for performance
            var scatter = _avaPlot.Plot.Add.Scatter(xs, ys);
            scatter.LineWidth = GetLineWidth(seriesIndex);
            scatter.Color = ParseColor(series.Color);
            scatter.MarkerSize = 0; // Line only for performance with large datasets
            
            // Add min/max/avg info to legend
            if (ys.Length > 0)
            {
                var min = ys.Min();
                var max = ys.Max();
                var avg = ys.Average();
                scatter.LegendText = $"{series.Name} (min:{min:F2} max:{max:F2} avg:{avg:F2})";
                
                globalMin = Math.Min(globalMin, min);
                globalMax = Math.Max(globalMax, max);
                totalPoints += ys.Length;
            }
            else
            {
                scatter.LegendText = series.Name;
            }

            seriesIndex++;
        }

        // Configure axes
        _avaPlot.Plot.Axes.AutoScale();
        
        // X-axis styling
        _avaPlot.Plot.XLabel("Time (sec)");
        
        // Y-axis styling - show "Value" or custom label
        var yLabel = configuration.YAxis.Label ?? "Value";
        _avaPlot.Plot.YLabel(yLabel);
        
        // Title with data info
        if (totalPoints > 0)
        {
            var seriesCount = configuration.Series.Count(s => s.IsVisible && s.Points.Count > 0);
            _avaPlot.Plot.Title($"Flight Data - {seriesCount} series, {totalPoints:N0} points");
        }

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
