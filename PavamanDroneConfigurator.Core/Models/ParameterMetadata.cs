namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// Contains metadata about a parameter from ArduPilot documentation.
/// Similar to Mission Planner's parameter metadata system.
/// Follows the REAL ArduPilot XML field meanings.
/// </summary>
public class ParameterMetadata
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    
    /// <summary>
    /// Units symbol (e.g., "cm", "m/s", "deg")
    /// </summary>
    public string? Units { get; set; }
    
    /// <summary>
    /// Full text description of units (e.g., "centimeters", "meters per second")
    /// </summary>
    public string? UnitsText { get; set; }
    
    /// <summary>
    /// Minimum allowed value from Range field
    /// </summary>
    public float? Min { get; set; }
    
    /// <summary>
    /// Maximum allowed value from Range field
    /// </summary>
    public float? Max { get; set; }
    
    /// <summary>
    /// Step size for editing (NOT a default value!)
    /// This is the increment amount when adjusting the parameter.
    /// </summary>
    public float? StepSize { get; set; }
    
    /// <summary>
    /// Default value from the "Default" field in XML.
    /// If not specified in XML, this should be 0.
    /// </summary>
    public float DefaultValue { get; set; } = 0;
    
    /// <summary>
    /// Enum options for single-select parameters.
    /// Key = option value, Value = option label
    /// </summary>
    public Dictionary<string, string> Options { get; set; } = new();
    
    /// <summary>
    /// Legacy property for backward compatibility
    /// </summary>
    public float? MinValue 
    { 
        get => Min;
        set => Min = value;
    }
    
    /// <summary>
    /// Legacy property for backward compatibility
    /// </summary>
    public float? MaxValue 
    { 
        get => Max;
        set => Max = value;
    }
    
    /// <summary>
    /// Legacy property (use StepSize instead)
    /// </summary>
    [Obsolete("Use StepSize instead - Increment is step size, not default value")]
    public float? Increment 
    { 
        get => StepSize;
        set => StepSize = value;
    }
    
    /// <summary>
    /// Full range string from XML
    /// </summary>
    public string? Range { get; set; }
    
    /// <summary>
    /// Whether this parameter is read-only
    /// </summary>
    public bool ReadOnly { get; set; }
    
    /// <summary>
    /// Whether changing this parameter requires a reboot
    /// </summary>
    public bool RebootRequired { get; set; }
    
    /// <summary>
    /// Enum/Bitmask options as integer keys.
    /// For enum: single-select dropdown options
    /// For bitmask: multiple-select checkbox options
    /// </summary>
    public Dictionary<int, string>? Values { get; set; }
    
    /// <summary>
    /// Raw bitmask string from XML (e.g., "0:Roll,1:Pitch,2:Yaw")
    /// </summary>
    public string? Bitmask { get; set; }
    
    /// <summary>
    /// Whether this parameter has enum options (single-select dropdown)
    /// </summary>
    public bool HasEnumOptions => Values != null && Values.Count > 0 && string.IsNullOrEmpty(Bitmask);
    
    /// <summary>
    /// Whether this parameter has bitmask options (multiple-select checkboxes)
    /// </summary>
    public bool HasBitmaskOptions => !string.IsNullOrEmpty(Bitmask) && Values != null && Values.Count > 0;
    
    /// <summary>
    /// Whether this parameter has any options at all
    /// </summary>
    public bool HasOptions => Values != null && Values.Count > 0;
}
