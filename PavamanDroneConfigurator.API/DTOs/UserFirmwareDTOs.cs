using System;
using System.ComponentModel.DataAnnotations;

namespace PavamanDroneConfigurator.API.DTOs;

/// <summary>
/// Request to upload firmware for a specific user
/// </summary>
public class UploadUserFirmwareRequest
{
    [Required]
    public Guid UserId { get; set; }

    [MaxLength(255)]
    public string? CustomFileName { get; set; }

    [Required]
    [MaxLength(255)]
    public string FirmwareName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string FirmwareVersion { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(50)]
    public string VehicleType { get; set; } = "Copter";
}

/// <summary>
/// Response for user firmware upload
/// </summary>
public class UserFirmwareResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string S3Key { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? FirmwareName { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? Description { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileSizeDisplay { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public Guid UploadedBy { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastDownloaded { get; set; }
    public int DownloadCount { get; set; }
    public string? DownloadUrl { get; set; }
}

/// <summary>
/// Request to assign existing firmware to user
/// </summary>
public class AssignFirmwareRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public string S3Key { get; set; } = string.Empty;
}

/// <summary>
/// Request to update firmware metadata
/// </summary>
public class UpdateFirmwareMetadataRequest
{
    [MaxLength(255)]
    public string? FirmwareName { get; set; }

    [MaxLength(50)]
    public string? FirmwareVersion { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool? IsActive { get; set; }
}

/// <summary>
/// List of user firmwares response
/// </summary>
public class UserFirmwaresListResponse
{
    public List<UserFirmwareResponse> Firmwares { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// Firmware assignment summary (for admin dashboard)
/// </summary>
public class FirmwareAssignmentSummary
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public int FirmwareCount { get; set; }
    public List<UserFirmwareResponse> Firmwares { get; set; } = new();
}
