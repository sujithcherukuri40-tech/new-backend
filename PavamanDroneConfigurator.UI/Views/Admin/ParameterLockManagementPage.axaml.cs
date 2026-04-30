using System;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PavamanDroneConfigurator.UI.Models;
using PavamanDroneConfigurator.UI.ViewModels.Admin;

namespace PavamanDroneConfigurator.UI.Views.Admin;

public partial class ParameterLockManagementPage : UserControl
{
    public ParameterLockManagementPage()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ParameterLockManagementViewModel vm)
            vm.RefreshLocksCommand.Execute(null);
    }

    private void OnLockRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border &&
            border.DataContext is ParamLockModel lockModel &&
            DataContext is ParameterLockManagementViewModel vm)
        {
            vm.ViewLockDetailCommand.Execute(lockModel);
        }
    }

    private async void OnDownloadJsonClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterLockManagementViewModel vm) return;

        var json = vm.GetDetailLockJson();
        var safeName = (vm.DetailLock?.UserName ?? "export")
            .Replace(" ", "_")
            .Replace("/", "-");
        var fileName = $"param_lock_{safeName}_{DateTimeOffset.Now:yyyyMMdd_HHmm}.json";

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Parameter Lock JSON",
            SuggestedFileName = fileName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON File") { Patterns = new[] { "*.json" } }
            }
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(json));
        }
    }
}
