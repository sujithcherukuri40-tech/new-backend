using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PavamanDroneConfigurator.Core.Models;

public class DroneParameter : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private float _value;
    private float _originalValue;
    private string? _description;
    private float? _minValue;
    private float? _maxValue;
    private float? _defaultValue;
    private string? _units;
    private bool _isModified;
    private ObservableCollection<ParameterOption> _options = new();
    private ParameterOption? _selectedOption;
    private bool _isBitmask;
    private ObservableCollection<ParameterOption> _selectedBitmaskOptions = new();

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public float Value
    {
        get => _value;
        set
        {
            if (System.Math.Abs(_value - value) > 0.0001f)
            {
                _value = value;
                IsModified = System.Math.Abs(_value - _originalValue) > 0.0001f;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ValueDisplay));
                UpdateSelectedOptionFromValue();
            }
        }
    }

    /// <summary>
    /// Display string for the value (used in grid).
    /// </summary>
    public string ValueDisplay => _value.ToString("G");

    /// <summary>
    /// The original value from the vehicle. Used to track if the parameter has been modified.
    /// </summary>
    public float OriginalValue
    {
        get => _originalValue;
        set
        {
            if (System.Math.Abs(_originalValue - value) > 0.0001f)
            {
                _originalValue = value;
                IsModified = System.Math.Abs(_value - _originalValue) > 0.0001f;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Default value from metadata.
    /// </summary>
    public float? DefaultValue
    {
        get => _defaultValue;
        set
        {
            if (_defaultValue != value)
            {
                _defaultValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DefaultDisplay));
            }
        }
    }

    /// <summary>
    /// Display string for the default value.
    /// </summary>
    public string DefaultDisplay => _defaultValue?.ToString("G") ?? "NaN";

    /// <summary>
    /// Units for this parameter (e.g., "cm/s", "deg", "Hz").
    /// </summary>
    public string? Units
    {
        get => _units;
        set
        {
            if (_units != value)
            {
                _units = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Indicates whether the current value differs from the original value.
    /// </summary>
    public bool IsModified
    {
        get => _isModified;
        private set
        {
            if (_isModified != value)
            {
                _isModified = value;
                OnPropertyChanged();
            }
        }
    }

    public string? Description
    {
        get => _description;
        set
        {
            if (_description != value)
            {
                _description = value;
                OnPropertyChanged();
            }
        }
    }

    public float? MinValue
    {
        get => _minValue;
        set
        {
            if (_minValue != value)
            {
                _minValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RangeDisplay));
            }
        }
    }

    public float? MaxValue
    {
        get => _maxValue;
        set
        {
            if (_maxValue != value)
            {
                _maxValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RangeDisplay));
            }
        }
    }

    /// <summary>
    /// Available options for enum-type parameters (like Mission Planner).
    /// </summary>
    public ObservableCollection<ParameterOption> Options
    {
        get => _options;
        set
        {
            if (_options != value)
            {
                _options = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasOptions));
                UpdateSelectedOptionFromValue();
            }
        }
    }

    /// <summary>
    /// Whether this parameter has selectable options.
    /// </summary>
    public bool HasOptions => _options.Count > 0;

    /// <summary>
    /// Currently selected option (bound to ComboBox).
    /// When changed, updates the Value property.
    /// </summary>
    public ParameterOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (_selectedOption != value)
            {
                _selectedOption = value;
                OnPropertyChanged();
                
                // Update Value when option is selected
                if (value != null)
                {
                    Value = value.Value;
                }
            }
        }
    }

    /// <summary>
    /// Gets a formatted display string for the parameter's valid range.
    /// Returns "NA" if no min/max values are defined.
    /// </summary>
    public string RangeDisplay
    {
        get
        {
            if (!MinValue.HasValue && !MaxValue.HasValue)
            {
                return string.Empty;
            }
            
            var minStr = MinValue.HasValue ? MinValue.Value.ToString("G") : "";
            var maxStr = MaxValue.HasValue ? MaxValue.Value.ToString("G") : "";
            
            return $"{minStr} - {maxStr}";
        }
    }

    /// <summary>
    /// Display for the Options column showing range or options list.
    /// Like Mission Planner shows "-0.5 0.95" or "0:Disabled\n1:Enabled".
    /// </summary>
    public string OptionsDisplay
    {
        get
        {
            if (HasOptions)
            {
                // Show first few options like Mission Planner
                var display = string.Join("\n", Options.Take(4).Select(o => $"{o.Value}:{o.Label}"));
                if (Options.Count > 4)
                    display += "\n...";
                return display;
            }
            
            // Show range if no options
            if (MinValue.HasValue && MaxValue.HasValue)
            {
                return $"{MinValue:G} {MaxValue:G}";
            }
            
            return string.Empty;
        }
    }

    /// <summary>
    /// Indicates if this parameter is a bitmask type.
    /// </summary>
    public bool IsBitmask
    {
        get => _isBitmask;
        set
        {
            if (_isBitmask != value)
            {
                _isBitmask = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Selected bitmask options for bitmask-type parameters.
    /// </summary>
    public ObservableCollection<ParameterOption> SelectedBitmaskOptions
    {
        get => _selectedBitmaskOptions;
        set
        {
            if (_selectedBitmaskOptions != value)
            {
                _selectedBitmaskOptions = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Initializes the bitmask selection based on the current value.
    /// </summary>
    public void InitializeBitmaskFromValue()
    {
        if (!IsBitmask || Options.Count == 0)
            return;

        SelectedBitmaskOptions.Clear();
        var intValue = (int)System.Math.Round(_value);

        foreach (var option in Options)
        {
            // Check if this bit is set in the value
            if ((intValue & option.Value) != 0)
            {
                SelectedBitmaskOptions.Add(option);
            }
        }
    }

    /// <summary>
    /// Updates the parameter value from the selected bitmask options.
    /// </summary>
    public void UpdateValueFromBitmask()
    {
        if (!IsBitmask)
            return;

        var newValue = 0;
        foreach (var option in SelectedBitmaskOptions)
        {
            newValue |= option.Value;
        }

        Value = newValue;
    }

    /// <summary>
    /// Updates the SelectedOption based on current Value.
    /// </summary>
    private void UpdateSelectedOptionFromValue()
    {
        if (HasOptions)
        {
            var intValue = (int)System.Math.Round(_value);
            _selectedOption = Options.FirstOrDefault(o => o.Value == intValue);
            OnPropertyChanged(nameof(SelectedOption));
        }
    }

    /// <summary>
    /// Marks this parameter as saved by updating the original value to match the current value.
    /// </summary>
    public void MarkAsSaved()
    {
        _originalValue = _value;
        IsModified = false;
    }

    /// <summary>
    /// Reverts the value to the original value from the vehicle.
    /// </summary>
    public void RevertToOriginal()
    {
        Value = _originalValue;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Represents a selectable option for enum-type parameters.
/// Used in the Options ComboBox like Mission Planner.
/// </summary>
public class ParameterOption
{
    public int Value { get; set; }
    public string Label { get; set; } = string.Empty;
    
    /// <summary>
    /// Display format: "Value:Label" (e.g., "0:Disabled", "1:Enabled")
    /// </summary>
    public string Display => $"{Value}:{Label}";
    
    public override string ToString() => Display;
}
