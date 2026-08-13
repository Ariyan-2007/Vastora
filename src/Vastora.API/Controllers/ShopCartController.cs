using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Cart;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>A Customer's cart on their own Business — scope comes entirely from the JWT, never the route.</summary>
[Route("api/shop/cart")]
[Authorize(Roles = nameof(UserRole.Customer))]
public class ShopCartController(ICurrentUserContext currentUser, ICartService cartService) : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<CartResponse>> Get(CancellationToken ct)
    {
        var result = await cartService.GetAsync(CurrentUser.BusinessId, CurrentUser.UserId, ct);
        return Ok(result);
    }

    [HttpPost("items")]
    public async Task<ActionResult<CartResponse>> AddItem(AddCartItemRequest request, CancellationToken ct)
    {
        var result = await cartService.AddItemAsync(CurrentUser.TenantId, CurrentUser.BusinessId, CurrentUser.UserId, request, ct);
        return Ok(result);
    }

    [HttpPut("items/{productId}")]
    public async Task<ActionResult<CartResponse>> UpdateItem(string productId, UpdateCartItemRequest request, CancellationToken ct)
    {
        var result = await cartService.UpdateItemAsync(CurrentUser.BusinessId, CurrentUser.UserId, productId, request, ct);
        return Ok(result);
    }

    [HttpDelete("items/{productId}")]
    public async Task<ActionResult<CartResponse>> RemoveItem(string productId, CancellationToken ct)
    {
        var result = await cartService.RemoveItemAsync(CurrentUser.BusinessId, CurrentUser.UserId, productId, ct);
        return Ok(result);
    }

    [HttpPost("coupon")]
    public async Task<ActionResult<CartResponse>> ApplyCoupon(ApplyCartCouponRequest request, CancellationToken ct)
    {
        var result = await cartService.ApplyCouponAsync(CurrentUser.BusinessId, CurrentUser.UserId, request, ct);
        return Ok(result);
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await cartService.ClearAsync(CurrentUser.BusinessId, CurrentUser.UserId, ct);
        return NoContent();
    }
}
