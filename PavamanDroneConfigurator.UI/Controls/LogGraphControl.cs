using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PavamanDroneConfigurator.Core.Models;
using ScottPlot;
using ScottPlot.Avalonia;
using System.Collections.Generic;
using System.Linq;

namespace PavamanDroneConfigurator.UI.Controls;

/// <summary>
/// Production-ready log graph control using ScottPlot for real data visualization.
/// Mission Planner-style dark theme with multi-series support.
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
        ConfigureDarkTheme();

        Content = _avaPlot;
    }

    private void ConfigureDarkTheme()
    {
        if (_avaPlot == null) return;

        // Dark background
        _avaPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E1E");
        _avaPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1E1E1E");
        
        // Grid lines
        _avaPlot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#3E3E42");
        _avaPlot.Plot.Grid.MinorLineColor = ScottPlot.Color.FromHex("#2D2D30");
        
        // Axis styling
        _avaPlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#999999"));
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
        ConfigureDarkTheme();

        if (configuration == null || configuration.Series.Count == 0)
        {
            _avaPlot.Plot.Title("No Data");
            _avaPlot.Refresh();
            return;
        }

        // Add each data series
        foreach (var series in configuration.Series.Where(s => s.IsVisible && s.Points.Count > 0))
        {
            var xs = series.Points.Select(p => p.X).ToArray();
            var ys = series.Points.Select(p => p.Y).ToArray();

            if (xs.Length == 0) continue;

            var scatter = _avaPlot.Plot.Add.Scatter(xs, ys);
            scatter.LegendText = series.Name;
            scatter.LineWidth = (float)series.LineWidth;
            scatter.Color = ParseColor(series.Color);
            scatter.MarkerSize = 0; // Line only for performance
        }

        // Auto-scale
        _avaPlot.Plot.Axes.AutoScale();

        // X-axis label
        _avaPlot.Plot.XLabel("Time (sec)");
        
        // Y-axis label  
        var yLabel = configuration.YAxis.Label ?? "(10^-6)";
        _avaPlot.Plot.YLabel(yLabel);

        // Refresh the plot
        _avaPlot.Refresh();
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
}
