using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PavamanDroneConfigurator.UI.Models;

/// <summary>
/// UI model for parameter lock information.
/// </summary>
public partial class ParamLockModel : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private Guid _userId;

    [ObservableProperty]
    private string? _userName;

    [ObservableProperty]
    private string? _userEmail;

    [ObservableProperty]
    private string? _deviceId;

    [ObservableProperty]
    private int _paramCount;

    [ObservableProperty]
    private List<string> _lockedParams = new();

    [ObservableProperty]
    private DateTimeOffset _createdAt;

    [ObservableProperty]
    private Guid _createdBy;

    [ObservableProperty]
    private string? _createdByName;

    [ObservableProperty]
    private DateTimeOffset? _updatedAt;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isSelected;

    public string DeviceDisplay => string.IsNullOrWhiteSpace(DeviceId) ? "All Devices" : DeviceId;
    
    public string UserInitial => string.IsNullOrEmpty(UserName) ? "?" : UserName.Trim()[0].ToString().ToUpper();

    public string ParamCountDisplay => $"{ParamCount} parameter{(ParamCount != 1 ? "s" : "")}";
    
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("MMM dd, yyyy HH:mm");
    
    public string UpdatedAtDisplay => UpdatedAt?.ToLocalTime().ToString("MMM dd, yyyy HH:mm") ?? "Never";

    public string LockedParamsPreview => LockedParams.Count > 0 
        ? string.Join(", ", LockedParams.Take(3)) + (LockedParams.Count > 3 ? "..." : "")
        : "No parameters";
}

/// <summary>
/// UI model for parameter selection in lock creation dialog.
/// </summary>
public partial class ParameterItemModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _group;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCurrentlyLocked;

    /// <summary>
    /// Current/locked value for this parameter. Updated when admin types in the value field.
    /// </summary>
    [ObservableProperty]
    private float _value;

    // Backing field for the editable text representation of Value.
    private string _lockedValueText = "0";
    private bool _syncingText;

    /// <summary>
    /// String representation of Value for two-way TextBox binding.
    /// Parses to float on user input; synced back from Value changes.
    /// </summary>
    public string LockedValueText
    {
        get => _lockedValueText;
        set
        {
            if (_lockedValueText == value) return;
            _lockedValueText = value;
            OnPropertyChanged();
            if (_syncingText) return;
            if (float.TryParse(value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var f))
            {
                _syncingText = true;
                Value = f;
                _syncingText = false;
            }
        }
    }

    partial void OnValueChanged(float value)
    {
        if (_syncingText) return;
        var text = value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        if (_lockedValueText != text)
        {
            _lockedValueText = text;
            OnPropertyChanged(nameof(LockedValueText));
        }
    }
}

/// <summary>
/// UI model for user selection in parameter lock creation.
/// </summary>
public partial class UserItemModel : ObservableObject
{
    [ObservableProperty]
    private Guid _userId;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _role = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _existingLocksCount;

    public string DisplayText => $"{FullName} ({Email})";
    public string Initial => string.IsNullOrWhiteSpace(FullName) ? "?" : FullName.Trim()[0].ToString().ToUpper();

    public string LocksInfo => ExistingLocksCount > 0 
        ? $"{ExistingLocksCount} existing lock{(ExistingLocksCount != 1 ? "s" : "")}"
        : "No locks";
}
