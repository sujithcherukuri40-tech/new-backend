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
            if (topLevel == null) 
            {
                ShowError("Unable to access file system");
                return;
            }
            
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
                
                if (!string.IsNullOrEmpty(path))
                {
                    // Validate file exists and path is safe
                    if (!System.IO.File.Exists(path))
                    {
                        ShowError("Selected file does not exist");
                        return;
                    }
                    
                    // Check file size (reasonable limit: 50MB for firmware)
                    var fileInfo = new System.IO.FileInfo(path);
                    if (fileInfo.Length > 50 * 1024 * 1024)
                    {
                        ShowError("File size exceeds maximum (50MB)");
                        return;
                    }
                    
                    if (DataContext is FirmwarePageViewModel viewModel)
                    {
                        await viewModel.SetFirmwareFileAsync(path);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"File picker error: {ex.Message}");
            ShowError($"Error selecting file: {ex.Message}");
        }
    }
    
    private void ShowError(string message)
    {
        // Simple error display - in a production app, you might want a proper dialog
        System.Diagnostics.Debug.WriteLine($"Firmware file error: {message}");
        // You could also show a toast notification or update status in the ViewModel
    }
}
