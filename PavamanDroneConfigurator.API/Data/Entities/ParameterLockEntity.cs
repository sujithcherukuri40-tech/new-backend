using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Data.Entities;

/// <summary>
/// Represents admin-locked parameters for a specific user/device.
/// The actual parameter list is stored in S3 as JSON.
/// </summary>
[Table("parameter_locks")]
public class ParameterLockEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// User ID to whom this lock applies
    /// </summary>
    [Required]
    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Device/Drone ID (nullable for user-wide locks)
    /// </summary>
    [Column("device_id")]
    [MaxLength(100)]
    public string? DeviceId { get; set; }

    /// <summary>
    /// S3 key where the locked parameters JSON is stored
    /// </summary>
    [Required]
    [Column("s3_key")]
    [MaxLength(500)]
    public string S3Key { get; set; } = string.Empty;

    /// <summary>
    /// Number of parameters locked (for quick reference)
    /// </summary>
    [Column("param_count")]
    public int ParamCount { get; set; }

    /// <summary>
    /// When this lock was created
    /// </summary>
    [Required]
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Admin who created this lock
    /// </summary>
    [Required]
    [Column("created_by")]
    public Guid CreatedBy { get; set; }

    /// <summary>
    /// When this lock was last updated
    /// </summary>
    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Whether this lock is currently active
    /// </summary>
    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    // Navigation properties
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(CreatedBy))]
    public User? CreatedByUser { get; set; }
}
