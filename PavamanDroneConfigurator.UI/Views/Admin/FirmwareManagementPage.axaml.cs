using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PavamanDroneConfigurator.UI.ViewModels.Admin;
using System;
using System.Linq;

namespace PavamanDroneConfigurator.UI.Views.Admin;

public partial class FirmwareManagementPage : UserControl
{
    public FirmwareManagementPage()
    {
        InitializeComponent();
    }
    
    /// <summary>
    /// Opens file picker to select firmware file
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
                    new FilePickerFileType("Firmware Files")
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
