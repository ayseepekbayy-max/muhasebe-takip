using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailSettings> options, IConfiguration configuration, ILogger<EmailService> logger)
    {
        _settings = options.Value;
        _logger = logger;

        _settings.SmtpHost = Env(configuration, "SMTP_HOST", _settings.SmtpHost);
        _settings.SmtpPort = int.TryParse(Env(configuration, "SMTP_PORT", ""), out var port) ? port : _settings.SmtpPort;
        _settings.SmtpUsername = Env(configuration, "SMTP_USERNAME", _settings.SmtpUsername);
        _settings.SmtpPassword = Env(configuration, "SMTP_PASSWORD", _settings.SmtpPassword);
        _settings.FromEmail = Env(configuration, "SMTP_FROM_EMAIL", _settings.FromEmail);
        _settings.FromName = Env(configuration, "SMTP_FROM_NAME", _settings.FromName);
        _settings.EnableSsl = bool.TryParse(Env(configuration, "SMTP_ENABLE_SSL", ""), out var ssl) ? ssl : _settings.EnableSsl;
        _settings.AppBaseUrl = Env(configuration, "APP_BASE_URL", _settings.AppBaseUrl);
    }

    public async Task<EmailSendResult> SendAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidEmail(recipientEmail))
            return EmailSendResult.Hata("Geçerli bir e-posta adresi bulunamadı.");

        if (string.IsNullOrWhiteSpace(_settings.SmtpHost) ||
            string.IsNullOrWhiteSpace(_settings.SmtpUsername) ||
            string.IsNullOrWhiteSpace(_settings.SmtpPassword))
        {
            return EmailSendResult.Hata("SMTP ayarları eksik.");
        }

        var fromEmail = string.IsNullOrWhiteSpace(_settings.FromEmail)
            ? _settings.SmtpUsername
            : _settings.FromEmail;

        if (!IsValidEmail(fromEmail))
            return EmailSendResult.Hata("Gönderen e-posta ayarı geçersiz.");

        try
        {
            using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.EnableSsl,
                Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword)
            };

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, _settings.FromName),
                Subject = SanitizeSubject(subject),
                Body = htmlBody,
                IsBodyHtml = true
            };

            message.To.Add(recipientEmail);

            await client.SendMailAsync(message, cancellationToken);
            return EmailSendResult.Basarili();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "E-posta gönderimi başarısız oldu.");
            return EmailSendResult.Hata("E-posta gönderimi sırasında hata oluştu.");
        }
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var address = new MailAddress(email.Trim());
            return address.Address == email.Trim();
        }
        catch
        {
            return false;
        }
    }

    public static string SanitizeSubject(string subject)
    {
        return (subject ?? "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }

    private static string Env(IConfiguration configuration, string key, string fallback)
    {
        return Environment.GetEnvironmentVariable(key)
            ?? configuration[key]
            ?? fallback
            ?? "";
    }
}
