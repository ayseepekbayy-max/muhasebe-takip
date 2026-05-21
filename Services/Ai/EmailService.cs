using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace MuhasebeTakip2.App.Services;

public class EmailSettings
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public string SmtpPass { get; set; } = "";
    public string FromName { get; set; } = "FirmovaAI";
}

public class EmailService
{
    private readonly EmailSettings _settings;

    public EmailService(IOptions<EmailSettings> options)
    {
        _settings = options.Value;
    }

    public async Task SendAsync(string to, string subject, string body)
    {
        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_settings.SmtpUser, _settings.SmtpPass)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.SmtpUser, _settings.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };

        message.To.Add(to);

        await client.SendMailAsync(message);
    }
}