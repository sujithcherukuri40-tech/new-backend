using PavamanDroneConfigurator.Core.Enums;

namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// Information about a detected compass sensor.
/// Maps to ArduPilot COMPASS_ parameters.
/// </summary>
public class CompassInfo
{
    /// <summary>
    /// Compass index (1-3 for primary, secondary, tertiary)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Compass device ID (COMPASS_DEV_IDx)
    /// </summary>
    public int DeviceId { get; set; }

    /// <summary>
    /// Display name (e.g., "COMPASS 1", "COMPASS 2")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Bus type (I2C, SPI, CAN, etc.)
    /// </summary>
    public CompassBusType BusType { get; set; } = CompassBusType.Unknown;

    /// <summary>
    /// Whether this compass is external (COMPASS_EXTERNALx)
    /// </summary>
    public bool IsExternal { get; set; }

    /// <summary>
    /// Bus type display name
    /// </summary>
    public string BusTypeName => BusType.ToString();

    /// <summary>
    /// Whether this compass is enabled for use (COMPASS_USEx)
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether this compass is marked as missing/disabled
    /// </summary>
    public bool IsMissing => !IsEnabled;

    /// <summary>
    /// Extracted bus number from ArduPilot device ID (bits 16-23)
    /// </summary>
    public int BusNumber => (DeviceId >> 16) & 0xFF;

    /// <summary>
    /// Extracted device address from ArduPilot device ID (bits 8-15)
    /// </summary>
    public int Address => (DeviceId >> 8) & 0xFF;

    /// <summary>
    /// Extracted device type from ArduPilot device ID (bits 0-7)
    /// </summary>
    public int DeviceTypeCode => DeviceId & 0xFF;

    /// <summary>
    /// Device type code formatted as hex for display (e.g., 0x1A)
    /// </summary>
    public string DeviceTypeHex => $"0x{DeviceTypeCode:X2}";

    /// <summary>
    /// Orientation text placeholder (ArduPilot exposes this via parameters; default to None)
    /// </summary>
    public string Orientation { get; set; } = "None";

    /// <summary>
    /// Calibration status
    /// </summary>
    public CompassCalibrationStatus CalibrationStatus { get; set; } = CompassCalibrationStatus.NotCalibrated;

    /// <summary>
    /// Priority level (lower = higher priority)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// X offset calibration value (COMPASS_OFS_X / COMPASS_OFS2_X / COMPASS_OFS3_X)
    /// </summary>
    public float OffsetX { get; set; }

    /// <summary>
    /// Y offset calibration value
    /// </summary>
    public float OffsetY { get; set; }

    /// <summary>
    /// Z offset calibration value
    /// </summary>
    public float OffsetZ { get; set; }

    /// <summary>
    /// Motor compensation type (COMPASS_MOT_X)
    /// </summary>
    public float MotorCompensationX { get; set; }

    /// <summary>
    /// Motor compensation Y
    /// </summary>
    public float MotorCompensationY { get; set; }

    /// <summary>
    /// Motor compensation Z
    /// </summary>
    public float MotorCompensationZ { get; set; }

    /// <summary>
    /// Whether calibration data is available (offsets are non-zero)
    /// </summary>
    public bool HasCalibrationData => OffsetX != 0 || OffsetY != 0 || OffsetZ != 0;

    /// <summary>
    /// Gets the status display text
    /// </summary>
    public string StatusText => CalibrationStatus switch
    {
        CompassCalibrationStatus.NotCalibrated => "Not calibrated",
        CompassCalibrationStatus.CalibrationRequired => "Calibration required",
        CompassCalibrationStatus.InProgress => "Calibrating...",
        CompassCalibrationStatus.Calibrated => "Calibrated",
        CompassCalibrationStatus.Failed => "Calibration failed",
        _ => "Unknown"
    };
}

/// <summary>
/// Optical flow sensor settings.
/// Maps to ArduPilot FLOW_ parameters.
/// </summary>
public class FlowSensorSettings
{
    /// <summary>
    /// Flow sensor type/enable (FLOW_TYPE)
    /// </summary>
    public FlowType FlowType { get; set; } = FlowType.Disabled;

    /// <summary>
    /// Whether flow sensor is enabled
    /// </summary>
    public bool IsEnabled => FlowType != FlowType.Disabled;

    /// <summary>
    /// X-axis optical scale factor (FLOW_FXSCALER)
    /// Range: -200 to 200
    /// </summary>
    public float XAxisScaleFactor { get; set; }

    /// <summary>
    /// Y-axis optical scale factor (FLOW_FYSCALER)
    /// Range: -200 to 200
    /// </summary>
    public float YAxisScaleFactor { get; set; }

    /// <summary>
    /// Sensor yaw alignment in degrees (FLOW_ORIENT_YAW)
    /// Range: -180 to 180
    /// </summary>
    public float SensorYawAlignment { get; set; }

    /// <summary>
    /// Position X offset from CG in meters (FLOW_POS_X)
    /// </summary>
    public float PositionX { get; set; }

    /// <summary>
    /// Position Y offset from CG in meters (FLOW_POS_Y)
    /// </summary>
    public float PositionY { get; set; }

    /// <summary>
    /// Position Z offset from CG in meters (FLOW_POS_Z)
    /// </summary>
    public float PositionZ { get; set; }

    /// <summary>
    /// I2C bus address (FLOW_ADDR)
    /// </summary>
    public int I2CAddress { get; set; } = 0x42;
}

/// <summary>
/// Accelerometer calibration information
/// </summary>
public class AccelerometerInfo
{
    /// <summary>
    /// Accelerometer index (1-3)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Calibration status
    /// </summary>
    public AccelCalibrationStatus CalibrationStatus { get; set; } = AccelCalibrationStatus.NotCalibrated;

    /// <summary>
    /// X offset (INS_ACCOFFS_X)
    /// </summary>
    public float OffsetX { get; set; }

    /// <summary>
    /// Y offset (INS_ACCOFFS_Y)
    /// </summary>
    public float OffsetY { get; set; }

    /// <summary>
    /// Z offset (INS_ACCOFFS_Z)
    /// </summary>
    public float OffsetZ { get; set; }

    /// <summary>
    /// X scale (INS_ACCSCAL_X)
    /// </summary>
    public float ScaleX { get; set; } = 1.0f;

    /// <summary>
    /// Y scale (INS_ACCSCAL_Y)
    /// </summary>
    public float ScaleY { get; set; } = 1.0f;

    /// <summary>
    /// Z scale (INS_ACCSCAL_Z)
    /// </summary>
    public float ScaleZ { get; set; } = 1.0f;

    /// <summary>
    /// Whether calibration data is available
    /// </summary>
    public bool HasCalibrationData => OffsetX != 0 || OffsetY != 0 || OffsetZ != 0 ||
                                       ScaleX != 1.0f || ScaleY != 1.0f || ScaleZ != 1.0f;

    /// <summary>
    /// Status text for display
    /// </summary>
    public string StatusText => CalibrationStatus switch
    {
        AccelCalibrationStatus.NotCalibrated => "Not calibrated",
        AccelCalibrationStatus.Calibrated => "Calibrated",
        AccelCalibrationStatus.InProgress => "Calibrating...",
        _ => "Unknown"
    };
}

/// <summary>
/// Complete sensor calibration configuration
/// </summary>
public class SensorCalibrationConfiguration
{
    /// <summary>
    /// Available compass sensors (up to 3)
    /// </summary>
    public List<CompassInfo> Compasses { get; set; } = new();

    /// <summary>
    /// Accelerometer information
    /// </summary>
    public AccelerometerInfo Accelerometer { get; set; } = new();

    /// <summary>
    /// Optical flow sensor settings
    /// </summary>
    public FlowSensorSettings FlowSensor { get; set; } = new();

    /// <summary>
    /// Whether accelerometer sensor is available/detected
    /// </summary>
    public bool IsAccelAvailable { get; set; } = true;

    /// <summary>
    /// Whether gyroscope sensor is available/detected
    /// </summary>
    public bool IsGyroAvailable { get; set; } = true;

    /// <summary>
    /// Whether barometer sensor is available/detected
    /// </summary>
    public bool IsBaroAvailable { get; set; } = true;

    /// <summary>
    /// Whether accelerometer is calibrated
    /// </summary>
    public bool IsAccelCalibrated { get; set; }

    /// <summary>
    /// Whether level horizon is calibrated
    /// </summary>
    public bool IsLevelCalibrated { get; set; }

    /// <summary>
    /// Whether barometer is calibrated
    /// </summary>
    public bool IsBaroCalibrated { get; set; }

    /// <summary>
    /// Primary compass index (COMPASS_PRIO1_ID)
    /// </summary>
    public int PrimaryCompassIndex { get; set; } = 1;
}

/// <summary>
/// PDRL-compliant sensor calibration defaults
/// </summary>
public static class SensorCalibrationDefaults
{
    /// <summary>
    /// Flow sensor X scale factor default
    /// </summary>
    public const float FlowXScaleDefault = 0f;

    /// <summary>
    /// Flow sensor Y scale factor default
    /// </summary>
    public const float FlowYScaleDefault = 0f;

    /// <summary>
    /// Flow sensor yaw alignment default
    /// </summary>
    public const float FlowYawAlignmentDefault = 0f;

    /// <summary>
    /// Gets PDRL-compliant default flow settings
    /// </summary>
    public static FlowSensorSettings GetDefaultFlowSettings()
    {
        return new FlowSensorSettings
        {
            FlowType = FlowType.Disabled,
            XAxisScaleFactor = FlowXScaleDefault,
            YAxisScaleFactor = FlowYScaleDefault,
            SensorYawAlignment = FlowYawAlignmentDefault,
            PositionX = 0f,
            PositionY = 0f,
            PositionZ = 0f,
            I2CAddress = 0x42
        };
    }

    /// <summary>
    /// Validate sensor configuration for PDRL compliance
    /// </summary>
    public static List<string> ValidateConfiguration(SensorCalibrationConfiguration config)
    {
        var warnings = new List<string>();

        // Check accelerometer calibration
        if (!config.IsAccelCalibrated)
        {
            warnings.Add("Accelerometer not calibrated - required for PDRL compliance");
        }

        // Check compass calibration
        var enabledCompasses = config.Compasses.Where(c => c.IsEnabled).ToList();
        if (enabledCompasses.Count == 0)
        {
            warnings.Add("No compass enabled - GPS heading only may not meet PDRL requirements");
        }
        else
        {
            foreach (var compass in enabledCompasses)
            {
                if (compass.CalibrationStatus == CompassCalibrationStatus.NotCalibrated ||
                    compass.CalibrationStatus == CompassCalibrationStatus.CalibrationRequired)
                {
                    warnings.Add($"{compass.DisplayName} requires calibration");
                }
            }
        }

        // Check level calibration
        if (!config.IsLevelCalibrated)
        {
            warnings.Add("Level horizon not calibrated - recommended for accurate attitude");
        }

        return warnings;
    }
}
