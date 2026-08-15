using Microsoft.Extensions.Logging;
using Vastora.Application.Common.Interfaces;

namespace Vastora.Infrastructure.Notifications;

/// <summary>
/// Default INotificationService: logs at Warning level instead of actually sending anything —
/// there is no email/SMS provider configured (§9.10). This makes every notification visible in
/// the server log (including, notably, password-reset tokens — see AuthService) so the feature
/// is genuinely usable in development/demo, but it is not a substitute for real delivery.
/// </summary>
public class LoggingNotificationService(ILogger<LoggingNotificationService> logger) : INotificationService
{
    public Task NotifyAsync(NotificationMessage message, CancellationToken ct = default)
    {
        logger.LogWarning(
            "NOTIFICATION (no email/SMS provider configured) -> {Recipient} | {Subject} | {Body}",
            message.RecipientEmail, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}
