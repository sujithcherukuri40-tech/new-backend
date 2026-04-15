using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Core.Interfaces;

public interface IEmailService
{
    Task SendOtpEmailAsync(string email, string otp);

    /// <summary>
    /// Sends a password reset email with a 6-digit OTP code.
    /// </summary>
    /// <param name="email">Recipient email address.</param>
    /// <param name="fullName">Recipient full name for personalisation.</param>
    /// <param name="code">6-digit reset code.</param>
    Task SendPasswordResetEmailAsync(string email, string fullName, string code);
}
