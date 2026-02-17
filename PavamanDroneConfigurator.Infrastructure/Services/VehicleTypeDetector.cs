using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Infrastructure.MAVLink;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Detects ArduPilot vehicle type from MAVLink heartbeat messages.
/// Used to determine which parameter metadata set to load (Copter or Plane).
/// </summary>
public class VehicleTypeDetector
{
    private readonly ILogger<VehicleTypeDetector> _logger;

    // MAV_TYPE constants from MAVLink
    private const byte MAV_TYPE_QUADROTOR = 2;
    private const byte MAV_TYPE_COAXIAL = 3;
    private const byte MAV_TYPE_HELICOPTER = 4;
    private const byte MAV_TYPE_HEXAROTOR = 13;
    private const byte MAV_TYPE_OCTOROTOR = 14;
    private const byte MAV_TYPE_TRICOPTER = 15;
    private const byte MAV_TYPE_FIXED_WING = 1;
    private const byte MAV_TYPE_GROUND_ROVER = 10;
    private const byte MAV_TYPE_SURFACE_BOAT = 11;
    private const byte MAV_TYPE_SUBMARINE = 12;
    private const byte MAV_TYPE_ANTENNA_TRACKER = 5;

    // MAV_AUTOPILOT constants
    private const byte MAV_AUTOPILOT_ARDUPILOTMEGA = 3;

    public VehicleTypeDetector(ILogger<VehicleTypeDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detects vehicle type from MAVLink heartbeat data.
    /// </summary>
    public VehicleType DetectFromHeartbeat(HeartbeatData heartbeat)
    {
        // Only detect ArduPilot vehicles
        if (heartbeat.Autopilot != MAV_AUTOPILOT_ARDUPILOTMEGA)
        {
            _logger.LogWarning("Non-ArduPilot autopilot detected: {Autopilot}", heartbeat.Autopilot);
            return VehicleType.Unknown;
        }

        var vehicleType = heartbeat.VehicleType switch
        {
            // Copters
            MAV_TYPE_QUADROTOR => VehicleType.Copter,
            MAV_TYPE_HELICOPTER => VehicleType.Copter,
            MAV_TYPE_HEXAROTOR => VehicleType.Copter,
            MAV_TYPE_OCTOROTOR => VehicleType.Copter,
            MAV_TYPE_TRICOPTER => VehicleType.Copter,
            MAV_TYPE_COAXIAL => VehicleType.Copter,

            // Planes
            MAV_TYPE_FIXED_WING => VehicleType.Plane,

            // Rovers (not supported yet, fallback to Copter)
            MAV_TYPE_GROUND_ROVER => VehicleType.Copter,
            MAV_TYPE_SURFACE_BOAT => VehicleType.Copter,

            // Subs (not supported yet, fallback to Copter)
            MAV_TYPE_SUBMARINE => VehicleType.Copter,

            // Trackers (not supported yet, fallback to Copter)
            MAV_TYPE_ANTENNA_TRACKER => VehicleType.Copter,

            // Default to Copter (most common)
            _ => VehicleType.Copter
        };

        _logger.LogInformation("Detected vehicle type: {VehicleType} (MAVType: {MavType}, Autopilot: {Autopilot})", 
            vehicleType, heartbeat.VehicleType, heartbeat.Autopilot);

        return vehicleType;
    }

    /// <summary>
    /// Detects vehicle type from raw MAV_TYPE byte value.
    /// </summary>
    public VehicleType DetectFromMavType(byte mavType)
    {
        return mavType switch
        {
            MAV_TYPE_QUADROTOR => VehicleType.Copter,
            MAV_TYPE_HELICOPTER => VehicleType.Copter,
            MAV_TYPE_HEXAROTOR => VehicleType.Copter,
            MAV_TYPE_OCTOROTOR => VehicleType.Copter,
            MAV_TYPE_TRICOPTER => VehicleType.Copter,
            MAV_TYPE_COAXIAL => VehicleType.Copter,
            MAV_TYPE_FIXED_WING => VehicleType.Plane,
            _ => VehicleType.Copter
        };
    }

    /// <summary>
    /// Gets friendly name for vehicle type.
    /// </summary>
    public string GetVehicleTypeName(VehicleType vehicleType)
    {
        return vehicleType switch
        {
            VehicleType.Copter => "ArduCopter (Multicopter)",
            VehicleType.Plane => "ArduPlane (Fixed Wing)",
            VehicleType.Rover => "ArduRover (Ground/Boat)",
            VehicleType.Sub => "ArduSub (Submarine/ROV)",
            VehicleType.Tracker => "AntennaTracker",
            _ => "Unknown Vehicle Type"
        };
    }

    /// <summary>
    /// Gets text icon for vehicle type (for UI display).
    /// Uses simple text characters for Avalonia compatibility.
    /// </summary>
    public string GetVehicleTypeIcon(VehicleType vehicleType)
    {
        return vehicleType switch
        {
            VehicleType.Copter => "?",      // Diamond (Copter)
            VehicleType.Plane => "?",       // Right triangle (Plane)
            VehicleType.Rover => "?",       // Square (Rover)
            VehicleType.Sub => "?",         // Circle (Sub)
            VehicleType.Tracker => "?",     // Inverted bullet (Tracker)
            _ => "?"
        };
    }
}
