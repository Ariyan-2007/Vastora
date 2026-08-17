using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Vastora.Application.Common.Interfaces;

namespace Vastora.Infrastructure.Notifications;

/// <summary>
/// Real outbound email over SMTP — every message goes out through the one configured account
/// (SmtpSettings.FromAddress), regardless of who triggered it (verification, password reset,
/// order updates, lifecycle sweeps, ...). Selected in place of LoggingNotificationService in
/// DependencyInjection.AddInfrastructure whenever Smtp:Host is configured.
/// </summary>
public class SmtpNotificationService(IOptions<SmtpSettings> options, ILogger<SmtpNotificationService> logger) : INotificationService
{
    private readonly SmtpSettings _settings = options.Value;

    public async Task NotifyAsync(NotificationMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.RecipientEmail));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_settings.Username, _settings.Password, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            // Fire-and-forget by contract (INotificationService) — a bad recipient address or a
            // transient SMTP outage must never turn a successful order/registration into a failed
            // response to the caller. Logged loudly instead, so delivery failures are still visible.
            logger.LogError(ex, "Failed to send email to {Recipient} with subject {Subject}", message.RecipientEmail, message.Subject);
        }
    }
}
