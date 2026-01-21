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
    private float _defaultValue = 0;
    private float _stepSize = 1;
    private string? _units;
    private bool _isModified;
    private ObservableCollection<ParameterOption> _options = new();
    private ParameterOption? _selectedOption;
    private bool _isBitmask;
    private ObservableCollection<ParameterOption> _selectedBitmaskOptions = new();
    private string? _validationError;
    private string _optionsInputText = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBitmask));
            }
        }
    }

    public float Value
    {
        get => _value;
        set
        {
            // STRICT VALIDATION: Revert to DefaultValue if out of range
            float validatedValue = value;
            bool isOutOfRange = false;

            if (MinValue.HasValue && value < MinValue.Value)
            {
                validatedValue = DefaultValue;
                isOutOfRange = true;
                ValidationError = $"Value {value:G} is below minimum {MinValue.Value:G}. Reverted to default {DefaultValue:G}.";
            }
            else if (MaxValue.HasValue && value > MaxValue.Value)
            {
                validatedValue = DefaultValue;
                isOutOfRange = true;
                ValidationError = $"Value {value:G} exceeds maximum {MaxValue.Value:G}. Reverted to default {DefaultValue:G}.";
            }
            else
            {
                ValidationError = null;
            }

            if (System.Math.Abs(_value - validatedValue) > 0.0001f)
            {
                _value = validatedValue;
                IsModified = System.Math.Abs(_value - _originalValue) > 0.0001f;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ValueDisplay));
                UpdateSelectedOptionFromValue();

                // If we reverted to default, notify UI to clear invalid input
                if (isOutOfRange)
                {
                    OnPropertyChanged(nameof(Value));
                }
            }
        }
    }

    public string ValueDisplay => _value.ToString("G");

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
    /// Default value from metadata. Defaults to 0 if not specified.
    /// </summary>
    public float DefaultValue
    {
        get => _defaultValue;
        set
        {
            if (System.Math.Abs(_defaultValue - value) > 0.0001f)
            {
                _defaultValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DefaultDisplay));
            }
        }
    }

    public string DefaultDisplay => _defaultValue.ToString("G");

    /// <summary>
    /// Step size for editing (increment amount when adjusting parameter)
    /// </summary>
    public float StepSize
    {
        get => _stepSize;
        set
        {
            if (System.Math.Abs(_stepSize - value) > 0.0001f)
            {
                _stepSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ValuePlaceholder));
            }
        }
    }

    /// <summary>
    /// Placeholder text showing Min, Max, and Step
    /// </summary>
    public string ValuePlaceholder
    {
        get
        {
            var parts = new List<string>();
            if (MinValue.HasValue)
                parts.Add($"Min: {MinValue.Value:G}");
            if (MaxValue.HasValue)
                parts.Add($"Max: {MaxValue.Value:G}");
            if (StepSize > 0)
                parts.Add($"Step: {StepSize:G}");
            return parts.Count > 0 ? string.Join("  ", parts) : "Enter value";
        }
    }

    /// <summary>
    /// Options column placeholder showing range
    /// </summary>
    public string OptionsPlaceholder
    {
        get
        {
            if (MinValue.HasValue && MaxValue.HasValue)
                return $"Min: {MinValue.Value:G}   Max: {MaxValue.Value:G}";
            else if (MinValue.HasValue)
                return $"Min: {MinValue.Value:G}";
            else if (MaxValue.HasValue)
                return $"Max: {MaxValue.Value:G}";
            return "Enter value";
        }
    }

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
    /// Validates a value against Min/Max constraints.
    /// Returns the value if valid, or DefaultValue if out of range.
    /// </summary>
    public float ValidateValue(float value, out bool isValid)
    {
        if (MinValue.HasValue && value < MinValue.Value)
        {
            isValid = false;
            return DefaultValue;
        }

        if (MaxValue.HasValue && value > MaxValue.Value)
        {
            isValid = false;
            return DefaultValue;
        }

        isValid = true;
        return value;
    }

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
                OnPropertyChanged(nameof(ValuePlaceholder));
                OnPropertyChanged(nameof(OptionsPlaceholder));
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
                OnPropertyChanged(nameof(ValuePlaceholder));
                OnPropertyChanged(nameof(OptionsPlaceholder));
            }
        }
    }

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
                OnPropertyChanged(nameof(HasEnumOptions));
                OnPropertyChanged(nameof(HasBitmaskOptions));
                OnPropertyChanged(nameof(IsBitmask));
                UpdateSelectedOptionFromValue();
            }
        }
    }

    /// <summary>
    /// Whether this parameter has any selectable options.
    /// </summary>
    public bool HasOptions => _options != null && _options.Count > 0;

    /// <summary>
    /// Whether this parameter has enum options (single-select dropdown)
    /// </summary>
    public bool HasEnumOptions => HasOptions && !IsBitmask;

    /// <summary>
    /// Whether this parameter has bitmask options (multiple-select checkboxes)
    /// </summary>
    public bool HasBitmaskOptions => IsBitmask;

    /// <summary>
    /// Indicates if this parameter is a bitmask type.
    /// </summary>
    public bool IsBitmask
    {
        get => _isBitmask || (HasOptions && (_name.Contains("_MASK") || _name.EndsWith("MASK") || _name.Contains("ENABLE")));
        set
        {
            if (_isBitmask != value)
            {
                _isBitmask = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasEnumOptions));
                OnPropertyChanged(nameof(HasBitmaskOptions));
            }
        }
    }

    /// <summary>
    /// Whether this parameter has no options (should show TextBox in OPTIONS column)
    /// </summary>
    public bool HasNoOptions => !HasOptions;

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

    public ParameterOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (_selectedOption != value)
            {
                _selectedOption = value;
                OnPropertyChanged();

                if (value != null)
                {
                    Value = value.Value;
                }
            }
        }
    }

    /// <summary>
    /// Validation error message (if value is out of range)
    /// </summary>
    public string? ValidationError
    {
        get => _validationError;
        private set
        {
            if (_validationError != value)
            {
                _validationError = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasValidationError));
            }
        }
    }

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

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

    public string OptionsDisplay
    {
        get
        {
            if (HasOptions)
            {
                var display = string.Join("\n", Options.Take(4).Select(o => $"{o.Value}:{o.Label}"));
                if (Options.Count > 4)
                    display += "\n...";
                return display;
            }

            if (MinValue.HasValue && MaxValue.HasValue)
            {
                return $"{MinValue:G} {MaxValue:G}";
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Gets the bitmask value from selected options.
    /// Supports both direct bit values and bit positions (using bitshift).
    /// </summary>
    public int GetBitmaskValue()
    {
        int result = 0;
        foreach (var opt in SelectedBitmaskOptions)
        {
            // Check if the value is already a bitmask (power of 2) or needs bitshift
            if (opt.Value > 0 && (opt.Value & (opt.Value - 1)) == 0)
            {
                // Value is already a power of 2, use it directly
                result |= opt.Value;
            }
            else
            {
                // Value is a bit position, use bitshift
                result |= (1 << opt.Value);
            }
        }
        return result;
    }

    /// <summary>
    /// Updates the parameter value from the selected bitmask options.
    /// </summary>
    public void UpdateValueFromBitmask()
    {
        if (!IsBitmask)
            return;

        Value = GetBitmaskValue();
    }

    /// <summary>
    /// Initializes the bitmask selection based on the current value.
    /// Supports both direct bit values and bit positions.
    /// </summary>
    public void InitializeBitmaskFromValue()
    {
        if (!IsBitmask || Options.Count == 0)
            return;

        SelectedBitmaskOptions.Clear();
        int intValue = (int)System.Math.Round(_value);

        foreach (var opt in Options)
        {
            bool isSelected = false;

            // Check if the value is already a bitmask (power of 2) or a bit position
            if (opt.Value > 0 && (opt.Value & (opt.Value - 1)) == 0)
            {
                // Value is already a power of 2, check directly
                isSelected = (intValue & opt.Value) != 0;
            }
            else
            {
                // Value is a bit position, use bitshift
                isSelected = (intValue & (1 << opt.Value)) != 0;
            }

            if (isSelected)
            {
                SelectedBitmaskOptions.Add(opt);
            }
        }
    }

    /// <summary>
    /// Updates the SelectedOption based on current Value.
    /// </summary>
    private void UpdateSelectedOptionFromValue()
    {
        if (HasOptions && !IsBitmask)
        {
            var intValue = (int)System.Math.Round(_value);
            _selectedOption = Options.FirstOrDefault(o => o.Value == intValue);
            OnPropertyChanged(nameof(SelectedOption));
        }
    }

    public void MarkAsSaved()
    {
        _originalValue = _value;
        IsModified = false;
        ValidationError = null;
    }

    public void RevertToOriginal()
    {
        Value = _originalValue;
        ValidationError = null;
    }

    /// <summary>
    /// Input text for the OPTIONS column. Always starts empty.
    /// User types here, and valid values are applied to Value property.
    /// </summary>
    public string OptionsInputText
    {
        get => _optionsInputText;
        set
        {
            if (_optionsInputText != value)
            {
                _optionsInputText = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Placeholder for OPTIONS column showing valid range
    /// </summary>
    public string RangePlaceholder
    {
        get
        {
            if (MinValue.HasValue && MaxValue.HasValue)
                return $"Min: {MinValue.Value:G}   Max: {MaxValue.Value:G}";
            else if (MinValue.HasValue)
                return $"Min: {MinValue.Value:G}";
            else if (MaxValue.HasValue)
                return $"Max: {MaxValue.Value:G}";
            return "Enter value";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ParameterOption
{
    public int Value { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Display => $"{Value}:{Label}";
    public override string ToString() => Display;
}