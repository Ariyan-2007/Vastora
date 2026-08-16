using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.API.Filters;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Orders;
using Vastora.Application.Returns;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>
/// Checkout, a customer's own orders, and returns. Checkout, preview, and guest lookup are
/// <c>[AllowAnonymous]</c> so guests can buy (§9.27); everything that lists or acts on order
/// history requires a Customer JWT. <c>[AllowAnonymous]</c> is applied per-action rather than on
/// the controller because ASP.NET Core lets a controller-level <c>[AllowAnonymous]</c> silently
/// override every action-level <c>[Authorize]</c> in the same controller.
/// </summary>
[Tags("Shop - Orders")]
[Route("api/shop/orders")]
public class ShopOrdersController(
    ICurrentUserContext currentUser,
    IOrderService orderService,
    IReturnService returnService) : VastoraControllerBase(currentUser)
{
    private const string CartTokenHeader = "X-Cart-Token";

    /// <summary>
    /// §9.17. Send an <c>Idempotency-Key</c> header and a retried request replays the original
    /// response instead of placing a second order. Optional — omitting it keeps the old behaviour.
    /// </summary>
    [HttpPost("checkout")]
    [Idempotent]
    [AllowAnonymous]
    public async Task<ActionResult<OrderResponse>> Checkout(
        CheckoutRequest request, [FromQuery] string? businessId, [FromQuery] string? tenantId, CancellationToken ct)
    {
        var result = await orderService.CheckoutAsync(
            ResolveTenantId(tenantId), ResolveBusinessId(businessId),
            NullIfEmpty(CurrentUser.UserId), GuestToken(), request, ct);

        return Ok(result);
    }

    /// <summary>Prices the cart without committing anything — no stock moves, no coupon usage burnt.</summary>
    [HttpPost("preview")]
    [AllowAnonymous]
    public async Task<ActionResult<CheckoutPreviewResponse>> Preview(
        CheckoutRequest request, [FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await orderService.PreviewAsync(
            ResolveBusinessId(businessId), NullIfEmpty(CurrentUser.UserId), GuestToken(), request, ct);

        return Ok(result);
    }

    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<PagedResult<OrderResponse>>> GetMine(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await orderService.GetForCustomerAsync(
            CurrentUser.BusinessId, CurrentUser.UserId, PageRequest.Of(page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>§9.27. A guest has no account to list orders under, so the order number plus the email that placed it is the key.</summary>
    [HttpGet("lookup")]
    [AllowAnonymous]
    public async Task<ActionResult<OrderResponse>> Lookup(
        [FromQuery] string businessId, [FromQuery] string orderNumber, [FromQuery] string email, CancellationToken ct)
    {
        var result = await orderService.LookupGuestOrderAsync(businessId, orderNumber, email, ct);
        return Ok(result);
    }

    [HttpGet("{orderId}")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<OrderResponse>> GetById(string orderId, CancellationToken ct)
    {
        var result = await orderService.GetByIdForCustomerAsync(CurrentUser.BusinessId, CurrentUser.UserId, orderId, ct);
        return Ok(result);
    }

    [HttpPost("{orderId}/cancel")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<OrderResponse>> Cancel(string orderId, CancellationToken ct)
    {
        var result = await orderService.CancelAsync(CurrentUser.BusinessId, CurrentUser.UserId, orderId, ct);
        return Ok(result);
    }

    // --- §9.21: customer-facing returns ---

    [HttpPost("returns")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<ReturnResponse>> RequestReturn(CreateReturnRequest request, CancellationToken ct)
    {
        var result = await returnService.RequestAsync(
            CurrentUser.TenantId, CurrentUser.BusinessId, CurrentUser.UserId, request, ct);
        return Ok(result);
    }

    [HttpGet("returns")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<PagedResult<ReturnResponse>>> GetMyReturns(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await returnService.GetForCustomerAsync(
            CurrentUser.BusinessId, CurrentUser.UserId, PageRequest.Of(page, pageSize), ct);
        return Ok(result);
    }

    [HttpPost("returns/{returnId}/cancel")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<ReturnResponse>> CancelReturn(string returnId, CancellationToken ct)
    {
        var result = await returnService.CancelAsync(CurrentUser.BusinessId, CurrentUser.UserId, returnId, ct);
        return Ok(result);
    }

    private string? GuestToken() =>
        Request.Headers[CartTokenHeader].ToString() is { Length: > 0 } token ? token : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private string ResolveBusinessId(string? fromQuery) =>
        !string.IsNullOrEmpty(CurrentUser.BusinessId)
            ? CurrentUser.BusinessId
            : fromQuery ?? throw new Application.Common.Exceptions.ConflictException(
                "businessId is required for a guest checkout.");

    private string ResolveTenantId(string? fromQuery) =>
        !string.IsNullOrEmpty(CurrentUser.TenantId) ? CurrentUser.TenantId : fromQuery ?? string.Empty;
}
