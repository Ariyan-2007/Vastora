using Vastora.Application.Common;

namespace Vastora.Application.Webhooks;

public record CreateWebhookRequest(string Url, List<string> Events, string Description, string BusinessId);

/// <summary>
/// <paramref name="Secret"/> is populated only in the response to the create call — it is stored
/// hashed and cannot be shown again. Rotate by deleting and recreating.
/// </summary>
public record WebhookResponse(
    string Id,
    string Url,
    List<string> Events,
    string Description,
    string BusinessId,
    bool IsActive,
    string? Secret,
    DateTime? LastDeliveryAt,
    int ConsecutiveFailures,
    DateTime? DisabledAt,
    DateTime CreatedAt);

public record WebhookDeliveryResponse(
    string Id,
    string EventName,
    int? ResponseStatusCode,
    string? Error,
    int AttemptCount,
    bool Succeeded,
    DateTime CreatedAt);

public interface IWebhookService
{
    Task<PagedResult<WebhookResponse>> GetAllAsync(string tenantId, PageRequest page, CancellationToken ct = default);

    Task<WebhookResponse> CreateAsync(string tenantId, CreateWebhookRequest request, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string webhookId, CancellationToken ct = default);

    Task<PagedResult<WebhookDeliveryResponse>> GetDeliveriesAsync(string tenantId, string webhookId, PageRequest page, CancellationToken ct = default);
}
