using System.ComponentModel.DataAnnotations;

namespace PavamanDroneConfigurator.API.DTOs;

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public required string Email { get; set; }
}

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public required string Email { get; set; }

    [Required(ErrorMessage = "Reset code is required")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Reset code must be 6 digits")]
    public required string Code { get; set; }

    [Required(ErrorMessage = "New password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
    public required string NewPassword { get; set; }
}
