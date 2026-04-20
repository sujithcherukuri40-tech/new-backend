using System.ComponentModel.DataAnnotations;

namespace PavamanDroneConfigurator.API.DTOs;

/// <summary>
/// Request DTO for user registration.
/// </summary>
public class RegisterRequest
{
    /// <summary>
    /// User's full name.
    /// </summary>
    [Required(ErrorMessage = "Full name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 100 characters")]
    public required string FullName { get; set; }

    /// <summary>
    /// User's email address.
    /// </summary>
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public required string Email { get; set; }

    /// <summary>
    /// User's password.
    /// </summary>
    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 12, ErrorMessage = "Password must be at least 12 characters")]
    public required string Password { get; set; }

    /// <summary>
    /// Password confirmation.
    /// </summary>
    [Required(ErrorMessage = "Confirm password is required")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match")]
    public required string ConfirmPassword { get; set; }
}
