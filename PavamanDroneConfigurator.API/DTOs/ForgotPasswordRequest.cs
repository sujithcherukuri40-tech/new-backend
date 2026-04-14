using System.ComponentModel.DataAnnotations;

namespace PavamanDroneConfigurator.API.DTOs;

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public required string Email { get; set; }
}
