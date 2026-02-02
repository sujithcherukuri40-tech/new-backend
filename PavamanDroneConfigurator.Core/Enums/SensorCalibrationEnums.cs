namespace PavamanDroneConfigurator.Core.Enums;

/// <summary>
/// Sensor calibration tab/type selection
/// </summary>
public enum SensorCalibrationTab
{
    /// <summary>Accelerometer calibration</summary>
    Accelerometer,
    
    /// <summary>Compass/Magnetometer calibration</summary>
    Compass,
    
    /// <summary>Level horizon calibration</summary>
    LevelHorizon,
    
    /// <summary>Pressure/Barometer calibration</summary>
    Pressure,
    
    /// <summary>Optical flow sensor settings</summary>
    Flow
}

/// <summary>
/// Compass bus types matching ArduPilot COMPASS_DEV_IDx bus field
/// </summary>
public enum CompassBusType
{
    /// <summary>Unknown bus type</summary>
    Unknown = 0,
    
    /// <summary>I2C bus</summary>
    I2C = 1,
    
    /// <summary>SPI bus</summary>
    SPI = 2,
    
    /// <summary>CAN bus (DroneCAN/UAVCAN)</summary>
    CAN = 3,
    
    /// <summary>Serial/UART</summary>
    Serial = 4
}

/// <summary>
/// Compass calibration status
/// </summary>
public enum CompassCalibrationStatus
{
    /// <summary>Not calibrated</summary>
    NotCalibrated,
    
    /// <summary>Calibration required</summary>
    CalibrationRequired,
    
    /// <summary>Calibration in progress</summary>
    InProgress,
    
    /// <summary>Calibrated successfully</summary>
    Calibrated,
    
    /// <summary>Calibration failed</summary>
    Failed
}

/// <summary>
/// Optical flow sensor enable state (FLOW_TYPE)
/// </summary>
public enum FlowType
{
    /// <summary>Disabled</summary>
    Disabled = 0,
    
    /// <summary>Raw sensor (PX4FLOW)</summary>
    RawSensor = 1,
    
    /// <summary>PX4FLOW via MAVLink</summary>
    PX4FlowMAVLink = 2,
    
    /// <summary>Bebop optical flow</summary>
    Bebop = 3,
    
    /// <summary>Pixart flow sensor</summary>
    Pixart = 4,
    
    /// <summary>VL53L1X flow sensor</summary>
    VL53L1X = 5,
    
    /// <summary>PMW3901 flow sensor</summary>
    PMW3901 = 6,
    
    /// <summary>UPFLOW sensor</summary>
    UPFLOW = 7
}

/// <summary>
/// Accelerometer calibration status
/// </summary>
public enum AccelCalibrationStatus
{
    /// <summary>Not calibrated</summary>
    NotCalibrated,
    
    /// <summary>Calibrated successfully</summary>
    Calibrated,
    
    /// <summary>Calibration in progress</summary>
    InProgress
}

/// <summary>
/// Barometer calibration status
/// </summary>
public enum BaroCalibrationStatus
{
    /// <summary>Not calibrated (using defaults)</summary>
    NotCalibrated,
    
    /// <summary>Calibrated</summary>
    Calibrated
}

/// <summary>
/// Level horizon calibration status
/// </summary>
public enum LevelCalibrationStatus
{
    /// <summary>Not calibrated</summary>
    NotCalibrated,
    
    /// <summary>Calibrated</summary>
    Calibrated,
    
    /// <summary>Calibration in progress</summary>
    InProgress
}
