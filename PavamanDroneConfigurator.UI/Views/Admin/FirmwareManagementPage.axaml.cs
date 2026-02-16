using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PavamanDroneConfigurator.UI.ViewModels.Admin;
using System;
using System.Linq;

namespace PavamanDroneConfigurator.UI.Views.Admin;

public partial class FirmwareManagementPage : UserControl
{
    private bool _isInitialized = false;

    public FirmwareManagementPage()
    {
        InitializeComponent();
        
        // Subscribe to AttachedToVisualTree to lazy load data
        AttachedToVisualTree += OnAttachedToVisualTree;
    }
    
    /// <summary>
    /// Initialize data when page becomes visible
    /// </summary>
    private async void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;
        
        if (DataContext is FirmwareManagementViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
    
    /// <summary>
    /// Opens file picker to select firmware file for upload.
    /// Supports all firmware file types: .hex, .bin, .apj, .px4
    /// </summary>
    public async void BrowseFirmware_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Firmware File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("All Firmware Files")
                    {
                        Patterns = new[] { "*.hex", "*.bin", "*.apj", "*.px4" },
                        MimeTypes = new[] { "application/octet-stream" }
                    },
                    new FilePickerFileType("Intel HEX Files (*.hex)")
                    {
                        Patterns = new[] { "*.hex" },
                        MimeTypes = new[] { "application/octet-stream" }
                    },
                    new FilePickerFileType("Binary Files (*.bin)")
                    {
                        Patterns = new[] { "*.bin" },
                        MimeTypes = new[] { "application/octet-stream" }
                    },
                    new FilePickerFileType("ArduPilot JSON (*.apj)")
                    {
                        Patterns = new[] { "*.apj" },
                        MimeTypes = new[] { "application/octet-stream" }
                    },
                    new FilePickerFileType("PX4 Firmware (*.px4)")
                    {
                        Patterns = new[] { "*.px4" },
                        MimeTypes = new[] { "application/octet-stream" }
                    },
                    new FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            });
            
            if (files.Count > 0)
            {
                var file = files[0];
                var path = file.TryGetLocalPath();
                
                if (!string.IsNullOrEmpty(path) && DataContext is FirmwareManagementViewModel viewModel)
                {
                    await viewModel.SetFirmwareFileAsync(path);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"File picker error: {ex.Message}");
        }
    }
}
