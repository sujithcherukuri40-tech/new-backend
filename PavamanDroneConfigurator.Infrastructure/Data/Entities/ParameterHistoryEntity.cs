using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PavamanDroneConfigurator.Infrastructure.Data.Entities;

/// <summary>
/// Tracks parameter changes over time for audit and rollback purposes.
/// Each record represents a parameter value at a specific point in time.
/// </summary>
[Table("parameter_history")]
public class ParameterHistoryEntity
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
    /// Parameter name (e.g., "SYSID_THISMAV", "COMPASS_AUTODEC")
    /// </summary>
    [Column("parameter_name")]
    [Required]
    [MaxLength(50)]
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>
    /// Parameter value as float
    /// </summary>
    [Column("parameter_value")]
    public float ParameterValue { get; set; }

    /// <summary>
    /// When this parameter was set
    /// </summary>
    [Column("changed_at")]
    public DateTime ChangedAt { get; set; }

    /// <summary>
    /// Who/what changed this parameter (e.g., "User", "Calibration", "Firmware")
    /// </summary>
    [Column("changed_by")]
    [MaxLength(100)]
    public string? ChangedBy { get; set; }

    /// <summary>
    /// Optional context about why this parameter was changed
    /// </summary>
    [Column("change_reason")]
    [MaxLength(500)]
    public string? ChangeReason { get; set; }

    // Navigation property
    [ForeignKey(nameof(DroneId))]
    public virtual DroneEntity Drone { get; set; } = null!;
}
