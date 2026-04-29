using System.ComponentModel.DataAnnotations;

namespace PavamanDroneConfigurator.API.DTOs;

/// <summary>
/// Request to create or update parameter locks for a user/device.
/// </summary>
public class CreateParamLockRequest
{
    /// <summary>
    /// User ID to lock parameters for
    /// </summary>
    [Required(ErrorMessage = "User ID is required")]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Device ID (optional - if null, applies to all user's devices)
    /// </summary>
    [MaxLength(100)]
    public string? DeviceId { get; set; }

    /// <summary>
    /// List of parameter keys to lock
    /// </summary>
    [Required(ErrorMessage = "At least one parameter must be specified")]
    [MinLength(1, ErrorMessage = "At least one parameter must be specified")]
    public required List<string> Params { get; set; }

    /// <summary>
    /// Drone values at the time of lock creation, keyed by parameter name.
    /// Used by clients to detect value drift on subsequent logins.
    /// </summary>
    public Dictionary<string, float> ParamValues { get; set; } = new();
}

/// <summary>
/// Request to update an existing parameter lock.
/// </summary>
public class UpdateParamLockRequest
{
    /// <summary>
    /// Lock ID to update
    /// </summary>
    [Required]
    public required int LockId { get; set; }

    /// <summary>
    /// Updated list of parameter keys
    /// </summary>
    [Required(ErrorMessage = "At least one parameter must be specified")]
    [MinLength(1, ErrorMessage = "At least one parameter must be specified")]
    public required List<string> Params { get; set; }

    /// <summary>
    /// Drone values at the time of lock update, keyed by parameter name.
    /// Used by clients to detect value drift on subsequent logins.
    /// </summary>
    public Dictionary<string, float> ParamValues { get; set; } = new();
}

/// <summary>
/// Response for parameter lock operations.
/// </summary>
public class ParamLockResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? S3Key { get; set; }
    public int? LockId { get; set; }
    public int ParamCount { get; set; }
}

/// <summary>
/// Request to check if parameters are locked.
/// </summary>
public class CheckLockedParamsRequest
{
    [Required]
    public required Guid UserId { get; set; }

    public string? DeviceId { get; set; }
}

/// <summary>
/// Response with locked parameters list.
/// </summary>
public class LockedParamsResponse
{
    public Guid UserId { get; set; }
    public string? DeviceId { get; set; }
    public List<string> LockedParams { get; set; } = new();
    public int Count { get; set; }
}
