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

    public async Task SendPasswordResetEmailAsync(string email, string fullName, string code)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required", nameof(code));

        try
        {
            var subject = "Your Password Reset Code — Kapil Future Tech";
            var encodedCode = HtmlEncoder.Default.Encode(code.Trim());
            var encodedName = HtmlEncoder.Default.Encode((fullName ?? "User").Trim());
            var htmlBody = $"""
                <!DOCTYPE html>
                <html lang="en">
                <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
                <body style="margin:0;padding:0;background:#F0F4FF;font-family:'Segoe UI',Arial,Helvetica,sans-serif;">
                  <table width="100%" cellpadding="0" cellspacing="0" style="background:#F0F4FF;padding:40px 0;">
                    <tr><td align="center">
                      <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.08);">
                        <!-- Header -->
                        <tr>
                          <td style="background:linear-gradient(135deg,#1E3A8A 0%,#3B82F6 100%);padding:32px 40px;text-align:center;">
                            <h1 style="margin:0;color:#ffffff;font-size:24px;font-weight:700;letter-spacing:-0.5px;">Kapil Future Tech</h1>
                            <p style="margin:6px 0 0;color:#BFDBFE;font-size:13px;">Drone Configurator</p>
                          </td>
                        </tr>
                        <!-- Body -->
                        <tr>
                          <td style="padding:36px 40px;">
                            <p style="margin:0 0 8px;color:#6B7280;font-size:14px;">Hello, {encodedName}</p>
                            <h2 style="margin:0 0 20px;color:#111827;font-size:20px;font-weight:600;">Reset your password</h2>
                            <p style="margin:0 0 24px;color:#374151;font-size:15px;line-height:1.6;">
                              We received a request to reset your password. Use the 6-digit code below to continue.
                              This code will expire in <strong>15 minutes</strong>.
                            </p>
                            <!-- OTP Box -->
                            <table width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 28px;">
                              <tr><td align="center">
                                <div style="display:inline-block;background:#EFF6FF;border:2px solid #93C5FD;border-radius:12px;padding:20px 48px;">
                                  <span style="font-size:40px;font-weight:800;color:#1E40AF;letter-spacing:12px;font-family:'Courier New',monospace;">{encodedCode}</span>
                                </div>
                              </td></tr>
                            </table>
                            <p style="margin:0 0 16px;color:#6B7280;font-size:13px;line-height:1.5;">
                              Enter this code in the Kapil Future Tech Drone Configurator app when prompted.
                              Do <strong>not</strong> share this code with anyone.
                            </p>
                            <p style="margin:0;color:#9CA3AF;font-size:12px;">
                              If you did not request a password reset, you can safely ignore this email. Your account remains secure.
                            </p>
                          </td>
                        </tr>
                        <!-- Footer -->
                        <tr>
                          <td style="background:#F8FAFC;padding:20px 40px;border-top:1px solid #E2E8F0;text-align:center;">
                            <p style="margin:0;color:#9CA3AF;font-size:12px;">&copy; 2025 Kapil Future Tech. All rights reserved.</p>
                          </td>
                        </tr>
                      </table>
                    </td></tr>
                  </table>
                </body>
                </html>
                """;

            await SendEmailInternalAsync(email, subject, htmlBody);
            _logger.LogInformation("Password reset OTP email sent to {Email}", email);
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
