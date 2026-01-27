namespace PavamanDroneConfigurator.Core.Enums;

/// <summary>
/// ArduPilot vehicle types for parameter metadata selection.
/// Matches Mission Planner's MAV_TYPE differentiation.
/// </summary>
public enum VehicleType
{
    /// <summary>
    /// Multicopter (quadcopter, hexacopter, octocopter, etc.)
    /// Uses apm.pdef.xml parameter definitions.
    /// Downloads arducopter.apj from platform directory.
    /// </summary>
    Copter = 0,

    /// <summary>
    /// Fixed-wing aircraft.
    /// Uses ArduPlane.pdef.xml parameter definitions.
    /// </summary>
    Plane = 1,

    /// <summary>
    /// Ground rover vehicle.
    /// Uses APMrover2.pdef.xml parameter definitions.
    /// </summary>
    Rover = 2,

    /// <summary>
    /// Submarine or ROV (Remotely Operated Vehicle).
    /// Uses ArduSub.pdef.xml parameter definitions.
    /// </summary>
    Sub = 3,

    /// <summary>
    /// Antenna tracker for following vehicles.
    /// Uses AntennaTracker.pdef.xml parameter definitions.
    /// </summary>
    Tracker = 4,

    /// <summary>
    /// Traditional helicopter with heli-specific firmware.
    /// IMPORTANT: This is DIFFERENT from Copter! Heli firmware uses:
    /// - URL: {platform}-heli/arducopter-heli.apj (NOT the standard copter firmware)
    /// - Platform folder: e.g., CubeOrangePlus-heli instead of CubeOrangePlus
    /// Uses Copter parameter definitions (same as standard copter).
    /// </summary>
    Helicopter = 5,

    /// <summary>
    /// Unknown or not yet detected vehicle type.
    /// Falls back to Copter parameters.
    /// </summary>
    Unknown = 99
}
