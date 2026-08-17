namespace Vastora.Application.Common.Interfaces;

/// <summary><paramref name="HtmlBody"/> is optional — null sends plain text only (e.g. a channel that doesn't render HTML).</summary>
public record NotificationMessage(string RecipientEmail, string Subject, string Body, string? HtmlBody = null);

/// <summary>
/// Fire-and-forget outbound notification (order confirmation, status changes, password reset).
/// One implementation today — LoggingNotificationService (Infrastructure), which just logs —
/// since no email/SMS provider is configured (Roadmap §9.10). Swap the DI registration for a
/// real provider (SendGrid, SES, Twilio, ...) later without touching any caller.
/// </summary>
public interface INotificationService
{
    Task NotifyAsync(NotificationMessage message, CancellationToken ct = default);
}
