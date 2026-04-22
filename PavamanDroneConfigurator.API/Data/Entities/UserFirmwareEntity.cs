using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Data.Entities;

/// <summary>
/// Represents a firmware file assigned to a specific user.
/// Admins can upload firmwares and assign them to specific users.
/// Only assigned users can see and download their firmwares.
/// </summary>
[Table("UserFirmwares")]
public class UserFirmwareEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    /// <summary>
    /// User ID this firmware is assigned to
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// S3 key/path for the firmware file
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string S3Key { get; set; } = string.Empty;

    /// <summary>
    /// Original firmware filename
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the firmware
    /// </summary>
    [MaxLength(255)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Firmware name/title set by admin
    /// </summary>
    [MaxLength(255)]
    public string? FirmwareName { get; set; }

    /// <summary>
    /// Firmware version
    /// </summary>
    [MaxLength(50)]
    public string? FirmwareVersion { get; set; }

    /// <summary>
    /// Description of the firmware
    /// </summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Vehicle type (Copter, Plane, Rover, etc.)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string VehicleType { get; set; } = "Copter";

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// When this firmware was uploaded
    /// </summary>
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Admin who uploaded this firmware
    /// </summary>
    [Required]
    public Guid UploadedBy { get; set; }

    /// <summary>
    /// When this firmware was assigned to the user
    /// </summary>
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Is this firmware active/visible to the user
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Last time user downloaded this firmware
    /// </summary>
    public DateTime? LastDownloaded { get; set; }

    /// <summary>
    /// Number of times downloaded
    /// </summary>
    public int DownloadCount { get; set; } = 0;

    /// <summary>
    /// Navigation property to User
    /// </summary>
    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// Navigation property to Admin who uploaded
    /// </summary>
    [ForeignKey("UploadedBy")]
    public virtual User UploadedByUser { get; set; } = null!;
}
