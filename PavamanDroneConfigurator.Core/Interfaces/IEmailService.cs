using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Core.Interfaces;

public interface IEmailService
{
    Task SendOtpEmailAsync(string email, string otp);

    /// <summary>
    /// Sends a password reset email with a 6-digit OTP code.
    /// </summary>
    Task SendPasswordResetEmailAsync(string email, string fullName, string code);

    /// <summary>
    /// Sends an account approval notification email.
    /// </summary>
    Task SendApprovalEmailAsync(string email, string fullName, bool approved);

    /// <summary>
    /// Sends a firmware assignment notification email.
    /// </summary>
    Task SendFirmwareAssignmentEmailAsync(string email, string fullName, string firmwareName, string firmwareVersion);
}
