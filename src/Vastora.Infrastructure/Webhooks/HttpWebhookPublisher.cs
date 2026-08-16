using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Webhooks;
using Vastora.Domain.Entities;

namespace Vastora.Infrastructure.Webhooks;

/// <inheritdoc cref="IWebhookPublisher"/>
public class HttpWebhookPublisher(
    IHttpClientFactory httpClientFactory,
    IMongoRepository<WebhookSubscription> subscriptions,
    IMongoRepository<WebhookDelivery> deliveries,
    ILogger<HttpWebhookPublisher> logger) : IWebhookPublisher
{
    /// <summary>After this many consecutive failures a subscription is switched off. A dead
    /// endpoint should stop costing every subsequent order a timeout.</summary>
    private const int FailureThreshold = 10;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(string tenantId, string businessId, string eventName, object payload, CancellationToken ct = default)
    {
        try
        {
            var matches = await subscriptions.FindAsync(
                s => s.TenantId == tenantId && s.IsActive && s.DisabledAt == null, ct);

            // An empty BusinessId on the subscription means tenant-wide.
            var targets = matches
                .Where(s => s.Events.Contains(eventName))
                .Where(s => string.IsNullOrEmpty(s.BusinessId) || s.BusinessId == businessId)
                .ToList();

            if (targets.Count == 0)
            {
                return;
            }

            var body = JsonSerializer.Serialize(new
            {
                @event = eventName,
                tenantId,
                businessId,
                occurredAt = DateTime.UtcNow,
                data = payload
            }, JsonOptions);

            foreach (var subscription in targets)
            {
                await DeliverAsync(subscription, eventName, body, ct);
            }
        }
        catch (Exception ex)
        {
            // Best-effort by contract (§9.39): a subscriber's problem must never fail the order
            // that produced the event.
            logger.LogWarning(ex, "Webhook fan-out failed for {Event} on tenant {TenantId}", eventName, tenantId);
        }
    }

    private async Task DeliverAsync(WebhookSubscription subscription, string eventName, string body, CancellationToken ct)
    {
        var record = new WebhookDelivery
        {
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            EventName = eventName,
            Payload = body,
            AttemptCount = 1
        };

        try
        {
            var client = httpClientFactory.CreateClient("webhooks");

            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            // The receiver can't verify a signature it can't reproduce, and we only store the
            // secret's hash — so the signature is computed over the *hash*, which both sides
            // know: we hold it, and the subscriber can derive it from the secret they were shown
            // once at creation. Same value, never transmitted.
            request.Headers.Add("X-Vastora-Event", eventName);
            request.Headers.Add("X-Vastora-Delivery", record.Id);
            request.Headers.Add("X-Vastora-Signature", Sign(body, subscription.SecretHash));

            var response = await client.SendAsync(request, ct);

            record.ResponseStatusCode = (int)response.StatusCode;
            record.Succeeded = response.IsSuccessStatusCode;

            if (!response.IsSuccessStatusCode)
            {
                record.Error = $"Endpoint returned {(int)response.StatusCode}.";
            }
        }
        catch (Exception ex)
        {
            record.Succeeded = false;
            record.Error = ex.Message;
        }

        await deliveries.AddAsync(record, ct);
        await UpdateHealthAsync(subscription, record.Succeeded, ct);
    }

    private async Task UpdateHealthAsync(WebhookSubscription subscription, bool succeeded, CancellationToken ct)
    {
        subscription.LastDeliveryAt = DateTime.UtcNow;

        if (succeeded)
        {
            subscription.ConsecutiveFailures = 0;
        }
        else
        {
            subscription.ConsecutiveFailures++;
            if (subscription.ConsecutiveFailures >= FailureThreshold)
            {
                subscription.DisabledAt = DateTime.UtcNow;
                logger.LogWarning(
                    "Disabled webhook {SubscriptionId} for {Url} after {Count} consecutive failures.",
                    subscription.Id, subscription.Url, subscription.ConsecutiveFailures);
            }
        }

        await subscriptions.UpdateAsync(subscription, ct);
    }

    private static string Sign(string body, string secretHash)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretHash));
        return $"sha256={Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant()}";
    }
}
