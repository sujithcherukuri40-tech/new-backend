using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Core.Interfaces;

public interface IEmailService
{
    Task SendOtpEmailAsync(string email, string otp);
    Task SendPasswordResetEmailAsync(string email, string resetLink);
}
