using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using System.Text.Encodings.Web;
using PavamanDroneConfigurator.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.Services.Aws;

public class SesEmailService : IEmailService
{
    private readonly IAmazonSimpleEmailService _sesClient;
    private readonly ILogger<SesEmailService> _logger;
    private readonly string _senderEmail;

    public SesEmailService(
        IAmazonSimpleEmailService sesClient,
        ILogger<SesEmailService> logger,
        IConfiguration configuration)
    {
        _sesClient = sesClient ?? throw new ArgumentNullException(nameof(sesClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _senderEmail = configuration["SES:SenderEmail"]
            ?? configuration["AWS:SES:SenderEmail"]
            ?? throw new ArgumentNullException("SES:SenderEmail configuration is missing");
    }

    public async Task SendOtpEmailAsync(string email, string otp)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));
        if (string.IsNullOrWhiteSpace(otp)) throw new ArgumentException("OTP is required", nameof(otp));

        try
        {
            var subject = "Your OTP Verification Code";
            var encodedOtp = HtmlEncoder.Default.Encode(otp.Trim());
            var htmlBody = $"""
                <html>
                  <body style=\"font-family:Arial,Helvetica,sans-serif;background:#f8fafc;color:#0f172a;padding:24px;\">
                    <div style=\"max-width:560px;margin:auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;padding:24px;\">
                      <h2 style=\"margin-top:0;color:#1e40af;\">OTP Verification</h2>
                      <p>Your one-time verification code is:</p>
                      <p style=\"font-size:28px;letter-spacing:4px;font-weight:700;color:#111827;margin:18px 0;\">{encodedOtp}</p>
                      <p>This code will expire shortly. If you did not request this, please ignore this email.</p>
                    </div>
                  </body>
                </html>
                """;

            await SendEmailInternalAsync(email, subject, htmlBody);
            _logger.LogInformation("OTP email sent to {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email to {Email}", email);
            throw;
        }
    }

    public async Task SendPasswordResetEmailAsync(string email, string resetLink)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));
        if (string.IsNullOrWhiteSpace(resetLink)) throw new ArgumentException("Reset link is required", nameof(resetLink));

        try
        {
            var subject = "Password Reset Request";
            var safeLink = HtmlEncoder.Default.Encode(resetLink.Trim());
            var htmlBody = $"""
                <html>
                  <body style=\"font-family:Arial,Helvetica,sans-serif;background:#f8fafc;color:#0f172a;padding:24px;\">
                    <div style=\"max-width:560px;margin:auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;padding:24px;\">
                      <h2 style=\"margin-top:0;color:#1e40af;\">Reset your password</h2>
                      <p>We received a request to reset your password.</p>
                      <p style=\"margin:24px 0;\">
                        <a href=\"{safeLink}\" style=\"background:#2563eb;color:#ffffff;padding:10px 16px;border-radius:8px;text-decoration:none;\">Reset Password</a>
                      </p>
                      <p>If the button does not work, copy and paste this link into your browser:</p>
                      <p><a href=\"{safeLink}\">{safeLink}</a></p>
                      <p>If you did not request this, you can safely ignore this email.</p>
                    </div>
                  </body>
                </html>
                """;

            await SendEmailInternalAsync(email, subject, htmlBody);
            _logger.LogInformation("Password reset email sent to {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", email);
            throw;
        }
    }

    private async Task SendEmailInternalAsync(string toAddress, string subject, string htmlBody)
    {
        if (!IsValidEmail(toAddress))
        {
            throw new ArgumentException("Invalid recipient email format", nameof(toAddress));
        }

        var sendRequest = new SendEmailRequest
        {
            Source = _senderEmail,
            Destination = new Destination { ToAddresses = new List<string> { toAddress } },
            Message = new Message
            {
                Subject = new Content(subject),
                Body = new Body { Html = new Content(htmlBody) }
            }
        };

        await _sesClient.SendEmailAsync(sendRequest);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new System.Net.Mail.MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
