using System.Security.Cryptography;
using System.Text;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Application.Webhooks;

/// <inheritdoc cref="IWebhookService"/>
public class WebhookService(
    IMongoRepository<WebhookSubscription> subscriptions,
    IMongoRepository<WebhookDelivery> deliveries) : IWebhookService
{
    public async Task<PagedResult<WebhookResponse>> GetAllAsync(string tenantId, PageRequest page, CancellationToken ct = default)
    {
        var result = await subscriptions.FindPagedAsync(s => s.TenantId == tenantId, page, s => s.CreatedAt, ct: ct);
        return result.Map(s => Map(s, null));
    }

    public async Task<WebhookResponse> CreateAsync(string tenantId, CreateWebhookRequest request, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            // HTTPS only. A signed payload delivered over plaintext HTTP is still readable by
            // anyone on the path, and webhook payloads carry order and customer data.
            throw new ConflictException("Webhook URLs must be absolute HTTPS URLs.");
        }

        var unknown = request.Events.Except(WebhookEvents.All).ToList();
        if (unknown.Count > 0)
        {
            throw new ConflictException($"Unknown event name(s): {string.Join(", ", unknown)}.");
        }

        var secret = GenerateSecret();
        var subscription = new WebhookSubscription
        {
            TenantId = tenantId,
            BusinessId = request.BusinessId ?? string.Empty,
            Url = request.Url,
            Events = request.Events,
            Description = request.Description,
            SecretHash = Hash(secret)
        };

        await subscriptions.AddAsync(subscription, ct);
        return Map(subscription, secret);
    }

    public async Task DeleteAsync(string tenantId, string webhookId, CancellationToken ct = default)
    {
        var subscription = await subscriptions.GetByIdAsync(webhookId, ct);
        if (subscription is null || subscription.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(WebhookSubscription), webhookId);
        }

        await subscriptions.DeleteAsync(webhookId, ct: ct);
    }

    public async Task<PagedResult<WebhookDeliveryResponse>> GetDeliveriesAsync(string tenantId, string webhookId, PageRequest page, CancellationToken ct = default)
    {
        var result = await deliveries.FindPagedAsync(
            d => d.TenantId == tenantId && d.SubscriptionId == webhookId, page, d => d.CreatedAt, ct: ct);

        return result.Map(d => new WebhookDeliveryResponse(
            d.Id, d.EventName, d.ResponseStatusCode, d.Error, d.AttemptCount, d.Succeeded, d.CreatedAt));
    }

    internal static string GenerateSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static WebhookResponse Map(WebhookSubscription s, string? secret) => new(
        s.Id, s.Url, s.Events, s.Description, s.BusinessId, s.IsActive, secret,
        s.LastDeliveryAt, s.ConsecutiveFailures, s.DisabledAt, s.CreatedAt);
}
