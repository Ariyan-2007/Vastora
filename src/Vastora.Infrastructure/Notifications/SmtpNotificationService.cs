using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Infrastructure.Notifications;

/// <summary>
/// Real outbound email over SMTP. When <see cref="NotificationMessage.BusinessId"/> is set and
/// that Business has its own mail domain configured (via SuperOffice — see
/// Business.MailSettings), the connection and From identity for that message use that Business's
/// credentials instead of the platform's. Everything else — mail with no BusinessId (TenantOwner
/// password resets, subscription notices) and any Business that hasn't set one up — falls back
/// to the platform's own account (SmtpSettings). Selected in place of LoggingNotificationService
/// in DependencyInjection.AddInfrastructure whenever Smtp:Host is configured.
/// </summary>
public class SmtpNotificationService(
    IOptions<SmtpSettings> options,
    IMongoRepository<Business> businesses,
    ILogger<SmtpNotificationService> logger) : INotificationService
{
    private readonly SmtpSettings _platformSettings = options.Value;

    public async Task NotifyAsync(NotificationMessage message, CancellationToken ct = default)
    {
        var connection = await ResolveConnectionAsync(message.BusinessId, ct);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(connection.FromName, connection.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.RecipientEmail));
        mime.Subject = message.Subject;

        // BodyBuilder emits multipart/alternative when both are set, so clients that render HTML
        // show the branded template and everything else falls back to the plain-text part.
        var builder = new BodyBuilder { TextBody = message.Body };
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            builder.HtmlBody = message.HtmlBody;
        }

        mime.Body = builder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(connection.Host, connection.Port, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(connection.Username, connection.Password, ct);
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

    private async Task<SmtpSettings> ResolveConnectionAsync(string? businessId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(businessId))
        {
            return _platformSettings;
        }

        var business = await businesses.GetByIdAsync(businessId, ct);
        var mail = business?.MailSettings;
        if (mail is null || !mail.Enabled || string.IsNullOrWhiteSpace(mail.Host))
        {
            return _platformSettings;
        }

        return new SmtpSettings
        {
            Host = mail.Host,
            Port = mail.Port,
            Username = mail.Username,
            Password = mail.Password,
            FromAddress = string.IsNullOrWhiteSpace(mail.FromAddress) ? _platformSettings.FromAddress : mail.FromAddress,
            FromName = string.IsNullOrWhiteSpace(mail.FromName) ? business!.Name : mail.FromName
        };
    }
}
