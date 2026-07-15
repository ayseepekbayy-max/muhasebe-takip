namespace MuhasebeTakip2.App.Services;

public interface IEmailService
{
    Task<EmailSendResult> SendAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);
}
