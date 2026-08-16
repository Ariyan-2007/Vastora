using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.ApiKeys;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Webhooks;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>
/// §9.39. Tenant-facing integration surface: outbound webhooks and server-to-server API keys.
/// This is what turns Vastora from a closed product into a platform — before it, the only
/// credential in the system was a 30-minute user JWT, which no integration can use.
///
/// TenantOwner-only: these credentials span every Business the tenant owns, so they sit at the
/// same level as SuperOffice rather than inside one BackOffice.
/// </summary>
[Tags("SuperOffice - Integrations")]
[Route("api/integrations")]
[Authorize(Roles = nameof(UserRole.TenantOwner))]
public class IntegrationsController(
    ICurrentUserContext currentUser,
    IWebhookService webhookService,
    IApiKeyService apiKeyService) : VastoraControllerBase(currentUser)
{
    /// <summary>The event names a subscription may listen for.</summary>
    [HttpGet("webhooks/events")]
    public ActionResult<string[]> GetEventNames() => Ok(WebhookEvents.All);

    [HttpGet("webhooks")]
    public async Task<ActionResult<PagedResult<WebhookResponse>>> GetWebhooks(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await webhookService.GetAllAsync(CurrentUser.TenantId, PageRequest.Of(page, pageSize), ct));

    /// <summary>The signing secret is in this response and nowhere else. Rotate by deleting and recreating.</summary>
    [HttpPost("webhooks")]
    public async Task<ActionResult<WebhookResponse>> CreateWebhook(CreateWebhookRequest request, CancellationToken ct) =>
        Ok(await webhookService.CreateAsync(CurrentUser.TenantId, request, ct));

    [HttpDelete("webhooks/{webhookId}")]
    public async Task<IActionResult> DeleteWebhook(string webhookId, CancellationToken ct)
    {
        await webhookService.DeleteAsync(CurrentUser.TenantId, webhookId, ct);
        return NoContent();
    }

    /// <summary>Delivery attempts, so a tenant can debug their own endpoint without asking us.</summary>
    [HttpGet("webhooks/{webhookId}/deliveries")]
    public async Task<ActionResult<PagedResult<WebhookDeliveryResponse>>> GetDeliveries(
        string webhookId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await webhookService.GetDeliveriesAsync(CurrentUser.TenantId, webhookId, PageRequest.Of(page, pageSize), ct));

    [HttpGet("api-keys")]
    public async Task<ActionResult<PagedResult<ApiKeyResponse>>> GetApiKeys(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await apiKeyService.GetAllAsync(CurrentUser.TenantId, PageRequest.Of(page, pageSize), ct));

    /// <summary>The full key is in this response and nowhere else — only its hash is stored.</summary>
    [HttpPost("api-keys")]
    public async Task<ActionResult<ApiKeyResponse>> CreateApiKey(CreateApiKeyRequest request, CancellationToken ct) =>
        Ok(await apiKeyService.CreateAsync(CurrentUser.TenantId, request, ct));

    [HttpDelete("api-keys/{apiKeyId}")]
    public async Task<IActionResult> RevokeApiKey(string apiKeyId, CancellationToken ct)
    {
        await apiKeyService.RevokeAsync(CurrentUser.TenantId, apiKeyId, ct);
        return NoContent();
    }
}
