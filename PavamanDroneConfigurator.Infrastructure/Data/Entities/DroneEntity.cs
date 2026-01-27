using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PavamanDroneConfigurator.Infrastructure.Data.Entities;

/// <summary>
/// Represents a drone/vehicle that has been configured using the application.
/// Stores identity information and metadata.
/// </summary>
[Table("drones")]
public class DroneEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// Serial number from the flight controller
    /// </summary>
    [Column("serial_number")]
    [MaxLength(50)]
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Board type ID (e.g., 140 for CubeOrange)
    /// </summary>
    [Column("board_type")]
    public int? BoardType { get; set; }

    /// <summary>
    /// Board name (e.g., "CubeOrange", "Pixhawk4")
    /// </summary>
    [Column("board_name")]
    [MaxLength(100)]
    public string? BoardName { get; set; }

    /// <summary>
    /// ArduPilot firmware version
    /// </summary>
    [Column("firmware_version")]
    [MaxLength(50)]
    public string? FirmwareVersion { get; set; }

    /// <summary>
    /// Vehicle type (Copter, Plane, Rover, etc.)
    /// </summary>
    [Column("vehicle_type")]
    [MaxLength(50)]
    public string? VehicleType { get; set; }

    /// <summary>
    /// User-assigned friendly name for the drone
    /// </summary>
    [Column("friendly_name")]
    [MaxLength(200)]
    public string? FriendlyName { get; set; }

    /// <summary>
    /// Optional notes about this drone
    /// </summary>
    [Column("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// When this drone was first added to the database
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last time this drone was connected/updated
    /// </summary>
    [Column("last_connected_at")]
    public DateTime? LastConnectedAt { get; set; }

    // Navigation properties
    public virtual ICollection<ParameterHistoryEntity> ParameterHistory { get; set; } = new List<ParameterHistoryEntity>();
    public virtual ICollection<CalibrationRecordEntity> CalibrationRecords { get; set; } = new List<CalibrationRecordEntity>();
}
