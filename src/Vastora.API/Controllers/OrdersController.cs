using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Orders;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>BackOffice order management, plus a Delivery Agent's own assigned-order queue.</summary>
[Route("api/businesses/{businessId}/orders")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)},{nameof(UserRole.DeliveryAgent)}")]
public class OrdersController(ICurrentUserContext currentUser, IOrderService orderService, IBusinessService businessService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
    public async Task<ActionResult<List<OrderResponse>>> GetAll(string businessId, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await orderService.GetForBusinessAsync(tenantId, businessId, ct);
        return Ok(result);
    }

    [HttpGet("assigned-to-me")]
    public async Task<ActionResult<List<OrderResponse>>> GetAssignedToMe(string businessId, CancellationToken ct)
    {
        await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await orderService.GetAssignedToAgentAsync(businessId, CurrentUser.UserId, ct);
        return Ok(result);
    }

    [HttpGet("{orderId}")]
    public async Task<ActionResult<OrderResponse>> GetById(string businessId, string orderId, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await orderService.GetByIdForBusinessAsync(tenantId, businessId, orderId, ct);
        return Ok(result);
    }

    [HttpPatch("{orderId}/status")]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(string businessId, string orderId, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await orderService.UpdateStatusAsync(tenantId, businessId, orderId, request, ct);
        return Ok(result);
    }

    [HttpPatch("{orderId}/assign-delivery")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
    public async Task<ActionResult<OrderResponse>> AssignDelivery(string businessId, string orderId, AssignDeliveryAgentRequest request, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await orderService.AssignDeliveryAgentAsync(tenantId, businessId, orderId, request, ct);
        return Ok(result);
    }
}
