using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Enums;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// Represents a file format option for export.
/// </summary>
public sealed record ExportFormatOption(ExportFileFormat Format, string DisplayName, string Extension);

/// <summary>
/// ViewModel for the Export Parameters dialog.
/// </summary>
public partial class ExportDialogViewModel : ViewModelBase
{
    /// <summary>
    /// Available export file formats.
    /// </summary>
    public ObservableCollection<ExportFormatOption> FileFormats { get; } =
    [
        new ExportFormatOption(ExportFileFormat.Csv, "CSV (.csv)", ".csv"),
        new ExportFormatOption(ExportFileFormat.Params, "ArduPilot Parameters (.params)", ".params"),
        new ExportFormatOption(ExportFileFormat.Cfg, "Configuration File (.cfg)", ".cfg"),
        new ExportFormatOption(ExportFileFormat.Json, "JSON (.json)", ".json"),
        new ExportFormatOption(ExportFileFormat.Yaml, "YAML (.yaml)", ".yaml")
    ];

    [ObservableProperty]
    private string _fileName = "drone_parameters";

    [ObservableProperty]
    private ExportFormatOption? _selectedFormat;

    [ObservableProperty]
    private string? _selectedFilePath;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationError;

    [ObservableProperty]
    private bool _isExporting;

    /// <summary>
    /// Gets whether the dialog can proceed with export.
    /// </summary>
    public bool CanExport => !string.IsNullOrWhiteSpace(FileName) && 
                             SelectedFormat != null && 
                             !string.IsNullOrWhiteSpace(SelectedFilePath) &&
                             !HasValidationError &&
                             !IsExporting;

    /// <summary>
    /// Event raised when the dialog should close with a result.
    /// </summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>
    /// Gets the full file path with the correct extension.
    /// </summary>
    public string FullFilePath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SelectedFilePath) || SelectedFormat == null)
                return string.Empty;

            var fileName = SanitizeFileName(FileName);
            var extension = SelectedFormat.Extension;
            
            // Ensure the filename has the correct extension
            if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                fileName += extension;

            return System.IO.Path.Combine(SelectedFilePath, fileName);
        }
    }

    public ExportDialogViewModel()
    {
        // Default to CSV format
        SelectedFormat = FileFormats[0];
    }

    partial void OnFileNameChanged(string value)
    {
        ValidateFileName();
        OnPropertyChanged(nameof(CanExport));
    }

    partial void OnSelectedFormatChanged(ExportFormatOption? value)
    {
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(FullFilePath));
    }

    partial void OnSelectedFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(FullFilePath));
    }

    private void ValidateFileName()
    {
        if (string.IsNullOrWhiteSpace(FileName))
        {
            ValidationMessage = "File name cannot be empty.";
            HasValidationError = true;
            return;
        }

        // Check for invalid characters
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        foreach (var c in FileName)
        {
            if (Array.IndexOf(invalidChars, c) >= 0)
            {
                ValidationMessage = $"File name contains invalid character: '{c}'";
                HasValidationError = true;
                return;
            }
        }

        // Check for reserved Windows names
        string[] reservedNames = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", 
                                   "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", 
                                   "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        var fileNameUpper = FileName.ToUpperInvariant();
        var fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileNameUpper);
        
        if (reservedNames.Contains(fileNameWithoutExt))
        {
            ValidationMessage = $"'{FileName}' is a reserved system name.";
            HasValidationError = true;
            return;
        }

        // Check for path traversal attempts
        if (FileName.Contains("..") || FileName.Contains("/") || FileName.Contains("\\"))
        {
            ValidationMessage = "File name cannot contain path separators.";
            HasValidationError = true;
            return;
        }

        // All validation passed
        ValidationMessage = string.Empty;
        HasValidationError = false;
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "drone_parameters";

        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new System.Text.StringBuilder();
        
        foreach (var c in fileName)
        {
            sanitized.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
        }

        var result = sanitized.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "drone_parameters" : result;
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, false);
    }

    [RelayCommand]
    private void Export()
    {
        if (!CanExport)
            return;

        CloseRequested?.Invoke(this, true);
    }
}
