using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Orders;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>A Customer's own orders and checkout — scope comes entirely from the JWT, never the route.</summary>
[Route("api/shop/orders")]
[Authorize(Roles = nameof(UserRole.Customer))]
public class ShopOrdersController(ICurrentUserContext currentUser, IOrderService orderService) : VastoraControllerBase(currentUser)
{
    [HttpPost("checkout")]
    public async Task<ActionResult<OrderResponse>> Checkout(CheckoutRequest request, CancellationToken ct)
    {
        var result = await orderService.CheckoutAsync(CurrentUser.TenantId, CurrentUser.BusinessId, CurrentUser.UserId, request, ct);
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<List<OrderResponse>>> GetMine(CancellationToken ct)
    {
        var result = await orderService.GetForCustomerAsync(CurrentUser.BusinessId, CurrentUser.UserId, ct);
        return Ok(result);
    }

    [HttpGet("{orderId}")]
    public async Task<ActionResult<OrderResponse>> GetById(string orderId, CancellationToken ct)
    {
        var result = await orderService.GetByIdForCustomerAsync(CurrentUser.BusinessId, CurrentUser.UserId, orderId, ct);
        return Ok(result);
    }

    [HttpPost("{orderId}/cancel")]
    public async Task<ActionResult<OrderResponse>> Cancel(string orderId, CancellationToken ct)
    {
        var result = await orderService.CancelAsync(CurrentUser.BusinessId, CurrentUser.UserId, orderId, ct);
        return Ok(result);
    }
}
