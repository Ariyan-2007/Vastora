using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Orders;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>BackOffice order management, plus a Delivery Agent's own assigned-order queue.</summary>
[Tags("BackOffice - Orders")]
[Route("api/businesses/{businessId}/orders")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)},{nameof(UserRole.DeliveryAgent)}")]
[Authorize(Policy = "BusinessMember")]
public class OrdersController(ICurrentUserContext currentUser, IOrderService orderService)
    : VastoraControllerBase(currentUser)
{
    /// <summary>§9.18. Paged and filtered server-side — this used to return every order the business had.</summary>
    [HttpGet]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
    public async Task<ActionResult<PagedResult<OrderResponse>>> GetAll(string businessId, [FromQuery] OrderQuery query, CancellationToken ct)
    {
        var result = await orderService.GetForBusinessAsync(ResolvedTenantId, businessId, query, ct);
        return Ok(result);
    }

    [HttpGet("assigned-to-me")]
    public async Task<ActionResult<PagedResult<OrderResponse>>> GetAssignedToMe(
        string businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await orderService.GetAssignedToAgentAsync(businessId, CurrentUser.UserId, PageRequest.Of(page, pageSize), ct);
        return Ok(result);
    }

    [HttpGet("{orderId}")]
    public async Task<ActionResult<OrderResponse>> GetById(string businessId, string orderId, CancellationToken ct)
    {
        var result = await orderService.GetByIdForBusinessAsync(ResolvedTenantId, businessId, orderId, ct);
        return Ok(result);
    }

    [HttpPatch("{orderId}/status")]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(string businessId, string orderId, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var result = await orderService.UpdateStatusAsync(ResolvedTenantId, businessId, orderId, request, ct);
        return Ok(result);
    }

    /// <summary>Manual payment recording for the cash-on-delivery flow (§9.6) — not staff-restricted beyond the controller's usual set.</summary>
    [HttpPatch("{orderId}/payment-status")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
    public async Task<ActionResult<OrderResponse>> UpdatePaymentStatus(string businessId, string orderId, UpdatePaymentStatusRequest request, CancellationToken ct)
    {
        var result = await orderService.UpdatePaymentStatusAsync(ResolvedTenantId, businessId, orderId, request, ct);
        return Ok(result);
    }

    [HttpPatch("{orderId}/assign-delivery")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
    public async Task<ActionResult<OrderResponse>> AssignDelivery(string businessId, string orderId, AssignDeliveryAgentRequest request, CancellationToken ct)
    {
        var result = await orderService.AssignDeliveryAgentAsync(ResolvedTenantId, businessId, orderId, request, ct);
        return Ok(result);
    }

    /// <summary>§9.20. Records an external courier shipment — the path for a Business with the delivery-agent module off.</summary>
    [HttpPatch("{orderId}/shipment")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
    public async Task<ActionResult<OrderResponse>> UpdateShipment(string businessId, string orderId, UpdateShipmentRequest request, CancellationToken ct)
    {
        var result = await orderService.UpdateShipmentAsync(ResolvedTenantId, businessId, orderId, request, ct);
        return Ok(result);
    }

    [HttpPatch("{orderId}/internal-note")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
    public async Task<ActionResult<OrderResponse>> UpdateInternalNote(string businessId, string orderId, UpdateOrderNoteRequest request, CancellationToken ct)
    {
        var result = await orderService.UpdateInternalNoteAsync(ResolvedTenantId, businessId, orderId, request, ct);
        return Ok(result);
    }

    /// <summary>§9.33. Assigns a gapless sequential invoice number on first call, then returns the same one.</summary>
    [HttpGet("{orderId}/invoice")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
    public async Task<ActionResult<InvoiceResponse>> GetInvoice(string businessId, string orderId, CancellationToken ct)
    {
        var result = await orderService.GetInvoiceAsync(ResolvedTenantId, businessId, orderId, ct);
        return Ok(result);
    }
}
