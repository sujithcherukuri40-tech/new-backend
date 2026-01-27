using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PavamanDroneConfigurator.Infrastructure.Data.Entities;

/// <summary>
/// Records calibration events and results for compliance and troubleshooting.
/// Tracks when calibrations were performed and their outcomes.
/// </summary>
[Table("calibration_records")]
public class CalibrationRecordEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the drone
    /// </summary>
    [Column("drone_id")]
    public int DroneId { get; set; }

    /// <summary>
    /// Type of calibration (e.g., "Accelerometer", "Compass", "RC", "ESC")
    /// </summary>
    [Column("calibration_type")]
    [Required]
    [MaxLength(50)]
    public string CalibrationType { get; set; } = string.Empty;

    /// <summary>
    /// When the calibration was started
    /// </summary>
    [Column("started_at")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the calibration completed (null if in progress or failed)
    /// </summary>
    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Calibration result: Success, Failed, Aborted
    /// </summary>
    [Column("result")]
    [Required]
    [MaxLength(20)]
    public string Result { get; set; } = "InProgress";

    /// <summary>
    /// Detailed result data (JSON format)
    /// Can store offsets, fitness values, etc.
    /// </summary>
    [Column("result_data")]
    public string? ResultData { get; set; }

    /// <summary>
    /// Any error messages or notes from the calibration
    /// </summary>
    [Column("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Firmware version at the time of calibration
    /// </summary>
    [Column("firmware_version")]
    [MaxLength(50)]
    public string? FirmwareVersion { get; set; }

    // Navigation property
    [ForeignKey(nameof(DroneId))]
    public virtual DroneEntity Drone { get; set; } = null!;
}
