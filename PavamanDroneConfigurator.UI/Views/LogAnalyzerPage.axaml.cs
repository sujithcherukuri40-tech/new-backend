using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.UI.Controls;
using PavamanDroneConfigurator.UI.ViewModels;
using System;
using System.Linq;

namespace PavamanDroneConfigurator.UI.Views
{
    public partial class LogAnalyzerPage : UserControl
    {
        private LogGraphControl? _graphControl;
        private LogMapControl? _mapControl;

        public LogAnalyzerPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            // Get reference to graph control
            _graphControl = this.FindControl<LogGraphControl>("GraphControl");
            
            // Get reference to map control
            _mapControl = this.FindControl<LogMapControl>("MapControl");

            // Find the parent window and set it on the ViewModel
            if (DataContext is LogAnalyzerPageViewModel viewModel)
            {
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
                
                // Find parent window
                var window = TopLevel.GetTopLevel(this) as Window;
                if (window != null)
                {
                    viewModel.SetParentWindow(window);
                }
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogAnalyzerPageViewModel.CurrentGraph) && _graphControl != null)
            {
                var viewModel = DataContext as LogAnalyzerPageViewModel;
                _graphControl.UpdateGraph(viewModel?.CurrentGraph);
            }
        }

        private void FieldItem_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is LogFieldInfo field)
            {
                // Toggle selection
                field.IsSelected = !field.IsSelected;

                // Notify ViewModel
                if (DataContext is LogAnalyzerPageViewModel viewModel)
                {
                    viewModel.OnFieldSelectionChanged(field);
                }
            }
        }

        private void ScriptFunction_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Core.Interfaces.ScriptFunctionInfo function)
            {
                if (DataContext is LogAnalyzerPageViewModel viewModel)
                {
                    viewModel.InsertScriptFunction(function);
                }
            }
        }

        private void ZoomToTrack_Click(object? sender, RoutedEventArgs e)
        {
            _mapControl?.ZoomToTrack();
        }

        private void CenterOnStart_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is LogAnalyzerPageViewModel viewModel && viewModel.GpsTrack.Count > 0)
            {
                var firstPoint = viewModel.GpsTrack.First();
                viewModel.MapCenterLat = firstPoint.Latitude;
                viewModel.MapCenterLng = firstPoint.Longitude;
            }
        }

        private void CenterOnEnd_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is LogAnalyzerPageViewModel viewModel && viewModel.GpsTrack.Count > 0)
            {
                var lastPoint = viewModel.GpsTrack.Last();
                viewModel.MapCenterLat = lastPoint.Latitude;
                viewModel.MapCenterLng = lastPoint.Longitude;
            }
        }

        private void AutoScale_Click(object? sender, RoutedEventArgs e)
        {
            _graphControl?.ResetZoom();
        }

        private void GraphLeft_Click(object? sender, RoutedEventArgs e)
        {
            _graphControl?.PanLeft();
        }

        private void GraphRight_Click(object? sender, RoutedEventArgs e)
        {
            _graphControl?.PanRight();
        }

        private void ZoomIn_Click(object? sender, RoutedEventArgs e)
        {
            _graphControl?.ZoomIn();
        }

        private void ZoomOut_Click(object? sender, RoutedEventArgs e)
        {
            _graphControl?.ZoomOut();
        }
    }
}
