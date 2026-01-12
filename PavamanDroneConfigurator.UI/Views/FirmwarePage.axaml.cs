using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PavamanDroneConfigurator.UI.ViewModels;
using System;
using System.Linq;

namespace PavamanDroneConfigurator.UI.Views;

public partial class FirmwarePage : UserControl
{
    public FirmwarePage()
    {
        InitializeComponent();
    }
    
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        // Find the Browse button and wire up the file picker
        // This is done in code-behind because file picker requires access to the TopLevel
    }
    
    /// <summary>
    /// Opens a file picker dialog and sets the selected firmware file
    /// </summary>
    public async void BrowseFirmwareFile_Click(object? sender, RoutedEventArgs e)
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
                    new FilePickerFileType("ArduPilot Firmware")
                    {
                        Patterns = new[] { "*.apj", "*.px4", "*.bin", "*.hex" },
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
                
                if (!string.IsNullOrEmpty(path) && DataContext is FirmwarePageViewModel viewModel)
                {
                    await viewModel.SetFirmwareFileAsync(path);
                }
            }
        }
        catch (Exception ex)
        {
            // Log error - in production you'd want proper error handling
            System.Diagnostics.Debug.WriteLine($"Error opening file picker: {ex.Message}");
        }
    }
}
