using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Service implementation for sensor configuration and extended calibration operations.
/// Reads/writes ArduPilot COMPASS_, INS_, FLOW_, AHRS_ parameters.
/// </summary>
public class SensorConfigService : ISensorConfigService
{
    private readonly ILogger<SensorConfigService> _logger;
    private readonly IParameterService _parameterService;
    private readonly IConnectionService _connectionService;

    public event EventHandler<SensorCalibrationConfiguration>? ConfigurationChanged;

    // ArduPilot parameter names
    private static class Parameters
    {
        // Compass parameters
        public const string COMPASS_DEV_ID = "COMPASS_DEV_ID";
        public const string COMPASS_DEV_ID2 = "COMPASS_DEV_ID2";
        public const string COMPASS_DEV_ID3 = "COMPASS_DEV_ID3";
        public const string COMPASS_USE = "COMPASS_USE";
        public const string COMPASS_USE2 = "COMPASS_USE2";
        public const string COMPASS_USE3 = "COMPASS_USE3";
        public const string COMPASS_EXTERNAL = "COMPASS_EXTERNAL";
        public const string COMPASS_EXTERNAL2 = "COMPASS_EXTERNAL2";
        public const string COMPASS_EXTERNAL3 = "COMPASS_EXTERNAL3";
        public const string COMPASS_OFS_X = "COMPASS_OFS_X";
        public const string COMPASS_OFS_Y = "COMPASS_OFS_Y";
        public const string COMPASS_OFS_Z = "COMPASS_OFS_Z";
        public const string COMPASS_OFS2_X = "COMPASS_OFS2_X";
        public const string COMPASS_OFS2_Y = "COMPASS_OFS2_Y";
        public const string COMPASS_OFS2_Z = "COMPASS_OFS2_Z";
        public const string COMPASS_OFS3_X = "COMPASS_OFS3_X";
        public const string COMPASS_OFS3_Y = "COMPASS_OFS3_Y";
        public const string COMPASS_OFS3_Z = "COMPASS_OFS3_Z";
        public const string COMPASS_PRIO1_ID = "COMPASS_PRIO1_ID";
        public const string COMPASS_PRIO2_ID = "COMPASS_PRIO2_ID";
        public const string COMPASS_PRIO3_ID = "COMPASS_PRIO3_ID";

        // Accelerometer/INS parameters
        public const string INS_ACCOFFS_X = "INS_ACCOFFS_X";
        public const string INS_ACCOFFS_Y = "INS_ACCOFFS_Y";
        public const string INS_ACCOFFS_Z = "INS_ACCOFFS_Z";
        public const string INS_ACCSCAL_X = "INS_ACCSCAL_X";
        public const string INS_ACCSCAL_Y = "INS_ACCSCAL_Y";
        public const string INS_ACCSCAL_Z = "INS_ACCSCAL_Z";

        // Flow sensor parameters
        public const string FLOW_TYPE = "FLOW_TYPE";
        public const string FLOW_FXSCALER = "FLOW_FXSCALER";
        public const string FLOW_FYSCALER = "FLOW_FYSCALER";
        public const string FLOW_ORIENT_YAW = "FLOW_ORIENT_YAW";
        public const string FLOW_POS_X = "FLOW_POS_X";
        public const string FLOW_POS_Y = "FLOW_POS_Y";
        public const string FLOW_POS_Z = "FLOW_POS_Z";
        public const string FLOW_ADDR = "FLOW_ADDR";

        // Level/AHRS parameters
        public const string AHRS_TRIM_X = "AHRS_TRIM_X";
        public const string AHRS_TRIM_Y = "AHRS_TRIM_Y";
        public const string AHRS_TRIM_Z = "AHRS_TRIM_Z";

        // Barometer parameters
        public const string GND_ABS_PRESS = "GND_ABS_PRESS";
        public const string GND_TEMP = "GND_TEMP";
    }

    public SensorConfigService(
        ILogger<SensorConfigService> logger,
        IParameterService parameterService,
        IConnectionService connectionService)
    {
        _logger = logger;
        _parameterService = parameterService;
        _connectionService = connectionService;
    }

    #region Helper Methods

    private async Task<float?> GetParameterValueAsync(string name)
    {
        var param = await _parameterService.GetParameterAsync(name);
        return param?.Value;
    }

    private async Task<bool> SetParameterValueAsync(string name, float value)
    {
        var result = await _parameterService.SetParameterAsync(name, value);
        if (result)
        {
            _logger.LogDebug("Set {Parameter} = {Value}", name, value);
        }
        else
        {
            _logger.LogWarning("Failed to set {Parameter} = {Value}", name, value);
        }
        return result;
    }

    private CompassBusType GetBusTypeFromDeviceId(int deviceId)
    {
        // ArduPilot device ID format: bus_type is in bits 24-26
        var busType = (deviceId >> 24) & 0x07;
        return busType switch
        {
            1 => CompassBusType.I2C,
            2 => CompassBusType.SPI,
            3 => CompassBusType.CAN,
            4 => CompassBusType.Serial,
            _ => CompassBusType.Unknown
        };
    }

    #endregion

    #region Compass Operations

    public async Task<List<CompassInfo>> GetCompassesAsync()
    {
        var compasses = new List<CompassInfo>();

        try
        {
            _logger.LogInformation("Loading compass configuration");

            // Check compass 1
            var devId1 = await GetParameterValueAsync(Parameters.COMPASS_DEV_ID);
            if (devId1.HasValue && devId1.Value != 0)
            {
                var compass = await GetCompassAsync(1);
                if (compass != null) compasses.Add(compass);
            }

            // Check compass 2
            var devId2 = await GetParameterValueAsync(Parameters.COMPASS_DEV_ID2);
            if (devId2.HasValue && devId2.Value != 0)
            {
                var compass = await GetCompassAsync(2);
                if (compass != null) compasses.Add(compass);
            }

            // Check compass 3
            var devId3 = await GetParameterValueAsync(Parameters.COMPASS_DEV_ID3);
            if (devId3.HasValue && devId3.Value != 0)
            {
                var compass = await GetCompassAsync(3);
                if (compass != null) compasses.Add(compass);
            }

            // Apply priority from parameters when available
            var prio1 = await GetParameterValueAsync(Parameters.COMPASS_PRIO1_ID) ?? 0;
            var prio2 = await GetParameterValueAsync(Parameters.COMPASS_PRIO2_ID) ?? 0;
            var prio3 = await GetParameterValueAsync(Parameters.COMPASS_PRIO3_ID) ?? 0;

            foreach (var c in compasses)
            {
                c.Priority = 99; // default low priority
                if (c.DeviceId == (int)prio1) c.Priority = 1;
                else if (c.DeviceId == (int)prio2) c.Priority = 2;
                else if (c.DeviceId == (int)prio3) c.Priority = 3;
            }

            // If no explicit priority, keep detection order
            int fallback = 4;
            foreach (var c in compasses.Where(c => c.Priority == 99))
            {
                c.Priority = fallback++;
            }

            // Order by priority and reassign sequential values for UI
            compasses = compasses.OrderBy(c => c.Priority).ToList();
            for (int i = 0; i < compasses.Count; i++)
            {
                compasses[i].Priority = i + 1;
            }

            _logger.LogInformation("Found {Count} compass sensor(s)", compasses.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading compass configuration");
        }

        return compasses;
    }

    public async Task<CompassInfo?> GetCompassAsync(int index)
    {
        try
        {
            var devIdParam = index switch
            {
                1 => Parameters.COMPASS_DEV_ID,
                2 => Parameters.COMPASS_DEV_ID2,
                3 => Parameters.COMPASS_DEV_ID3,
                _ => null
            };

            if (devIdParam == null) return null;

            var devId = await GetParameterValueAsync(devIdParam);
            if (!devId.HasValue || devId.Value == 0) return null;

            var useParam = index switch
            {
                1 => Parameters.COMPASS_USE,
                2 => Parameters.COMPASS_USE2,
                3 => Parameters.COMPASS_USE3,
                _ => null
            };

            var extParam = index switch
            {
                1 => Parameters.COMPASS_EXTERNAL,
                2 => Parameters.COMPASS_EXTERNAL2,
                3 => Parameters.COMPASS_EXTERNAL3,
                _ => null
            };

            var isEnabled = useParam != null && (await GetParameterValueAsync(useParam) ?? 1) > 0;
            var isExternal = extParam != null && (await GetParameterValueAsync(extParam) ?? 0) > 0;
            var offsets = await GetCompassOffsetsAsync(index);

            var compass = new CompassInfo
            {
                Index = index,
                DeviceId = (int)devId.Value,
                DisplayName = $"COMPASS {index}",
                BusType = GetBusTypeFromDeviceId((int)devId.Value),
                IsEnabled = isEnabled,
                IsExternal = isExternal,
                Priority = index,
                OffsetX = offsets.X,
                OffsetY = offsets.Y,
                OffsetZ = offsets.Z
            };

            // Determine calibration status
            compass.CalibrationStatus = compass.HasCalibrationData
                ? CompassCalibrationStatus.Calibrated
                : CompassCalibrationStatus.CalibrationRequired;

            return compass;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting compass {Index}", index);
            return null;
        }
    }

    public async Task<bool> SetCompassEnabledAsync(int index, bool enabled)
    {
        var param = index switch
        {
            1 => Parameters.COMPASS_USE,
            2 => Parameters.COMPASS_USE2,
            3 => Parameters.COMPASS_USE3,
            _ => null
        };

        if (param == null) return false;

        var result = await SetParameterValueAsync(param, enabled ? 1f : 0f);
        if (result)
        {
            _logger.LogInformation("Compass {Index} {State}", index, enabled ? "enabled" : "disabled");
        }
        return result;
    }

    public async Task<bool> SetCompassPriorityAsync(int compassIndex, int newPriority)
    {
        try
        {
            var compass = await GetCompassAsync(compassIndex);
            if (compass == null)
            {
                _logger.LogWarning("Compass {Index} not found for priority set", compassIndex);
                return false;
            }

            // Map priority slot (0-based from UI) to parameter
            var prioParams = new[]
            {
                Parameters.COMPASS_PRIO1_ID,
                Parameters.COMPASS_PRIO2_ID,
                Parameters.COMPASS_PRIO3_ID
            };

            if (newPriority < 0 || newPriority >= prioParams.Length)
            {
                _logger.LogInformation("Priority {Priority} outside supported slots, skipping parameter write", newPriority);
                return true;
            }

            // Read existing priority IDs
            var prioValues = new int[3];
            prioValues[0] = (int)(await GetParameterValueAsync(Parameters.COMPASS_PRIO1_ID) ?? 0);
            prioValues[1] = (int)(await GetParameterValueAsync(Parameters.COMPASS_PRIO2_ID) ?? 0);
            prioValues[2] = (int)(await GetParameterValueAsync(Parameters.COMPASS_PRIO3_ID) ?? 0);

            // Remove this device ID from any slot first
            for (int i = 0; i < prioValues.Length; i++)
            {
                if (prioValues[i] == compass.DeviceId)
                    prioValues[i] = 0;
            }

            // Set desired slot
            prioValues[newPriority] = compass.DeviceId;

            // Write back parameters
            bool success = true;
            success &= await SetParameterValueAsync(Parameters.COMPASS_PRIO1_ID, prioValues[0]);
            success &= await SetParameterValueAsync(Parameters.COMPASS_PRIO2_ID, prioValues[1]);
            success &= await SetParameterValueAsync(Parameters.COMPASS_PRIO3_ID, prioValues[2]);

            _logger.LogInformation("Compass {Index} priority updated: slot={Priority} deviceId={DeviceId}", compassIndex, newPriority + 1, compass.DeviceId);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting compass priority for {Index}", compassIndex);
            return false;
        }
    }

    public async Task<bool> IsCompassCalibrationRequiredAsync(int index)
    {
        var offsets = await GetCompassOffsetsAsync(index);
        return offsets.X == 0 && offsets.Y == 0 && offsets.Z == 0;
    }

    public async Task<(float X, float Y, float Z)> GetCompassOffsetsAsync(int index)
    {
        var xParam = index switch
        {
            1 => Parameters.COMPASS_OFS_X,
            2 => Parameters.COMPASS_OFS2_X,
            3 => Parameters.COMPASS_OFS3_X,
            _ => null
        };

        var yParam = index switch
        {
            1 => Parameters.COMPASS_OFS_Y,
            2 => Parameters.COMPASS_OFS2_Y,
            3 => Parameters.COMPASS_OFS3_Y,
            _ => null
        };

        var zParam = index switch
        {
            1 => Parameters.COMPASS_OFS_Z,
            2 => Parameters.COMPASS_OFS2_Z,
            3 => Parameters.COMPASS_OFS3_Z,
            _ => null
        };

        if (xParam == null || yParam == null || zParam == null)
            return (0, 0, 0);

        var x = await GetParameterValueAsync(xParam) ?? 0;
        var y = await GetParameterValueAsync(yParam) ?? 0;
        var z = await GetParameterValueAsync(zParam) ?? 0;

        return (x, y, z);
    }

    #endregion

    #region Accelerometer Operations

    public async Task<AccelerometerInfo> GetAccelerometerInfoAsync()
    {
        var info = new AccelerometerInfo { Index = 1 };

        try
        {
            var offsets = await GetAccelOffsetsAsync();
            var scales = await GetAccelScalesAsync();

            info.OffsetX = offsets.X;
            info.OffsetY = offsets.Y;
            info.OffsetZ = offsets.Z;
            info.ScaleX = scales.X;
            info.ScaleY = scales.Y;
            info.ScaleZ = scales.Z;

            info.CalibrationStatus = info.HasCalibrationData
                ? AccelCalibrationStatus.Calibrated
                : AccelCalibrationStatus.NotCalibrated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting accelerometer info");
        }

        return info;
    }

    public async Task<bool> IsAccelerometerCalibratedAsync()
    {
        var offsets = await GetAccelOffsetsAsync();
        var scales = await GetAccelScalesAsync();

        return (offsets.X != 0 || offsets.Y != 0 || offsets.Z != 0) ||
               (scales.X != 1.0f || scales.Y != 1.0f || scales.Z != 1.0f);
    }

    public async Task<(float X, float Y, float Z)> GetAccelOffsetsAsync()
    {
        var x = await GetParameterValueAsync(Parameters.INS_ACCOFFS_X) ?? 0;
        var y = await GetParameterValueAsync(Parameters.INS_ACCOFFS_Y) ?? 0;
        var z = await GetParameterValueAsync(Parameters.INS_ACCOFFS_Z) ?? 0;
        return (x, y, z);
    }

    public async Task<(float X, float Y, float Z)> GetAccelScalesAsync()
    {
        var x = await GetParameterValueAsync(Parameters.INS_ACCSCAL_X) ?? 1f;
        var y = await GetParameterValueAsync(Parameters.INS_ACCSCAL_Y) ?? 1f;
        var z = await GetParameterValueAsync(Parameters.INS_ACCSCAL_Z) ?? 1f;
        return (x, y, z);
    }

    #endregion

    #region Optical Flow Operations

    public async Task<FlowSensorSettings?> GetFlowSettingsAsync()
    {
        try
        {
            _logger.LogInformation("Loading flow sensor settings");

            var settings = new FlowSensorSettings
            {
                FlowType = (FlowType)(int)(await GetParameterValueAsync(Parameters.FLOW_TYPE) ?? 0),
                XAxisScaleFactor = await GetParameterValueAsync(Parameters.FLOW_FXSCALER) ?? 0,
                YAxisScaleFactor = await GetParameterValueAsync(Parameters.FLOW_FYSCALER) ?? 0,
                SensorYawAlignment = await GetParameterValueAsync(Parameters.FLOW_ORIENT_YAW) ?? 0,
                PositionX = await GetParameterValueAsync(Parameters.FLOW_POS_X) ?? 0,
                PositionY = await GetParameterValueAsync(Parameters.FLOW_POS_Y) ?? 0,
                PositionZ = await GetParameterValueAsync(Parameters.FLOW_POS_Z) ?? 0,
                I2CAddress = (int)(await GetParameterValueAsync(Parameters.FLOW_ADDR) ?? 0x42)
            };

            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting flow sensor settings");
            return null;
        }
    }

    public async Task<bool> UpdateFlowSettingsAsync(FlowSensorSettings settings)
    {
        try
        {
            _logger.LogInformation("Updating flow sensor settings");

            var success = true;
            success &= await SetParameterValueAsync(Parameters.FLOW_TYPE, (int)settings.FlowType);
            success &= await SetParameterValueAsync(Parameters.FLOW_FXSCALER, settings.XAxisScaleFactor);
            success &= await SetParameterValueAsync(Parameters.FLOW_FYSCALER, settings.YAxisScaleFactor);
            success &= await SetParameterValueAsync(Parameters.FLOW_ORIENT_YAW, settings.SensorYawAlignment);

            _logger.LogInformation("Flow settings update: success={Success}", success);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating flow settings");
            return false;
        }
    }

    public async Task<bool> SetFlowEnabledAsync(FlowType type)
    {
        return await SetParameterValueAsync(Parameters.FLOW_TYPE, (int)type);
    }

    public async Task<bool> SetFlowXScaleAsync(float scale)
    {
        return await SetParameterValueAsync(Parameters.FLOW_FXSCALER, scale);
    }

    public async Task<bool> SetFlowYScaleAsync(float scale)
    {
        return await SetParameterValueAsync(Parameters.FLOW_FYSCALER, scale);
    }

    public async Task<bool> SetFlowYawAlignmentAsync(float degrees)
    {
        return await SetParameterValueAsync(Parameters.FLOW_ORIENT_YAW, degrees);
    }

    #endregion

    #region Level Horizon Operations

    public async Task<bool> IsLevelCalibratedAsync()
    {
        var trims = await GetLevelTrimsAsync();
        return trims.X != 0 || trims.Y != 0;
    }

    public async Task<(float X, float Y, float Z)> GetLevelTrimsAsync()
    {
        var x = await GetParameterValueAsync(Parameters.AHRS_TRIM_X) ?? 0;
        var y = await GetParameterValueAsync(Parameters.AHRS_TRIM_Y) ?? 0;
        var z = await GetParameterValueAsync(Parameters.AHRS_TRIM_Z) ?? 0;
        return (x, y, z);
    }

    #endregion

    #region Barometer Operations

    public async Task<bool> IsBarometerCalibratedAsync()
    {
        var pressure = await GetGroundPressureAsync();
        return pressure > 0;
    }

    public async Task<float> GetGroundPressureAsync()
    {
        return await GetParameterValueAsync(Parameters.GND_ABS_PRESS) ?? 0;
    }

    #endregion

    #region Complete Configuration

    public async Task<SensorCalibrationConfiguration?> GetSensorConfigurationAsync()
    {
        try
        {
            _logger.LogInformation("Loading complete sensor configuration");

            var config = new SensorCalibrationConfiguration
            {
                Compasses = await GetCompassesAsync(),
                Accelerometer = await GetAccelerometerInfoAsync(),
                FlowSensor = await GetFlowSettingsAsync() ?? new FlowSensorSettings(),
                IsAccelCalibrated = await IsAccelerometerCalibratedAsync(),
                IsLevelCalibrated = await IsLevelCalibratedAsync(),
                IsBaroCalibrated = await IsBarometerCalibratedAsync()
            };

            ConfigurationChanged?.Invoke(this, config);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading sensor configuration");
            return null;
        }
    }

    public List<string> ValidateConfiguration(SensorCalibrationConfiguration config)
    {
        return SensorCalibrationDefaults.ValidateConfiguration(config);
    }

    #endregion
}
