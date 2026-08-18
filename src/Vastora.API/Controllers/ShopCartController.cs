using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Cart;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Pricing;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>
/// A shopper's cart. For a signed-in Customer the scope comes entirely from the JWT, never the
/// route. For a guest (§9.27) it comes from an <c>X-Cart-Token</c> header — the server mints one
/// on first write and returns it as <c>guestToken</c>; the client sends it back thereafter.
///
/// <c>[AllowAnonymous]</c> rather than Customer-only, because the whole point of guest checkout
/// is that a shopper can fill a cart before they have an account. Applied per-action rather than
/// on the controller because ASP.NET Core lets a controller-level <c>[AllowAnonymous]</c> silently
/// override the one action here — <see cref="Merge"/> — that requires a Customer JWT.
/// </summary>
[Tags("Shop - Cart")]
[Route("api/shop/cart")]
public class ShopCartController(ICurrentUserContext currentUser, ICartService cartService) : VastoraControllerBase(currentUser)
{
    private const string CartTokenHeader = "X-Cart-Token";

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<CartResponse>> Get([FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await cartService.GetAsync(ResolveBusinessId(businessId), ResolveOwner(), ct);
        return Ok(result);
    }

    [HttpPost("items")]
    [AllowAnonymous]
    public async Task<ActionResult<CartResponse>> AddItem(AddCartItemRequest request, [FromQuery] string? businessId, [FromQuery] string? tenantId, CancellationToken ct)
    {
        var result = await cartService.AddItemAsync(
            ResolveTenantId(tenantId), ResolveBusinessId(businessId), ResolveOwner(), request, ct);
        return Ok(result);
    }

    [HttpPut("items/{productId}")]
    [AllowAnonymous]
    public async Task<ActionResult<CartResponse>> UpdateItem(string productId, UpdateCartItemRequest request, [FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await cartService.UpdateItemAsync(ResolveBusinessId(businessId), ResolveOwner(), productId, request, ct);
        return Ok(result);
    }

    [HttpDelete("items/{productId}")]
    [AllowAnonymous]
    public async Task<ActionResult<CartResponse>> RemoveItem(string productId, [FromQuery] string? variantId, [FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await cartService.RemoveItemAsync(ResolveBusinessId(businessId), ResolveOwner(), productId, variantId, ct);
        return Ok(result);
    }

    [HttpPost("coupon")]
    [AllowAnonymous]
    public async Task<ActionResult<CartResponse>> ApplyCoupon(ApplyCartCouponRequest request, [FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await cartService.ApplyCouponAsync(ResolveBusinessId(businessId), ResolveOwner(), request, ct);
        return Ok(result);
    }

    /// <summary>§9.23. Separate from the legacy single coupon — several promotion codes can stack.</summary>
    [HttpPost("promotions")]
    [AllowAnonymous]
    public async Task<ActionResult<CartResponse>> ApplyPromotion(ApplyCartCouponRequest request, [FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await cartService.ApplyPromotionCodeAsync(ResolveBusinessId(businessId), ResolveOwner(), request, ct);
        return Ok(result);
    }

    [HttpDelete("promotions/{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<CartResponse>> RemovePromotion(string code, [FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await cartService.RemovePromotionCodeAsync(ResolveBusinessId(businessId), ResolveOwner(), code, ct);
        return Ok(result);
    }

    /// <summary>§9.43. Applies a gift card code to the cart itself, so its discount shows in the
    /// preview — previously a gift card only registered at final checkout, blind.</summary>
    [HttpPost("gift-cards")]
    [AllowAnonymous]
    public async Task<ActionResult<CartResponse>> ApplyGiftCard(ApplyCartCouponRequest request, [FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await cartService.ApplyGiftCardAsync(ResolveBusinessId(businessId), ResolveOwner(), request, ct);
        return Ok(result);
    }

    [HttpDelete("gift-cards/{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<CartResponse>> RemoveGiftCard(string code, [FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await cartService.RemoveGiftCardAsync(ResolveBusinessId(businessId), ResolveOwner(), code, ct);
        return Ok(result);
    }

    /// <summary>§9.43. Opts this cart in or out of spending store credit. Customer-only — a guest has no balance to opt into.</summary>
    [HttpPut("store-credit")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<CartResponse>> SetStoreCredit(SetCartStoreCreditRequest request, CancellationToken ct)
    {
        var result = await cartService.SetUseStoreCreditAsync(CurrentUser.BusinessId, ResolveOwner(), request.UseStoreCredit, ct);
        return Ok(result);
    }

    /// <summary>
    /// §9.44. Sets how this cart previews delivery — Pickup/Digital drop the delivery fee and
    /// shipping options from the response entirely, rather than showing a misleading $0. Guest and
    /// Customer both, unlike store credit: a shopper deciding to pick up in-store shouldn't need
    /// an account first. Preview-only — repeat the same choice as <c>CheckoutRequest.FulfillmentMethod</c>
    /// for it to actually be charged that way.
    /// </summary>
    [HttpPut("fulfillment-method")]
    [AllowAnonymous]
    public async Task<ActionResult<CartResponse>> SetFulfillmentMethod(SetCartFulfillmentMethodRequest request, [FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await cartService.SetFulfillmentMethodAsync(ResolveBusinessId(businessId), ResolveOwner(), request.FulfillmentMethod, ct);
        return Ok(result);
    }

    /// <summary>
    /// §9.43. Public coupon and promotion codes worth showing this shopper right now — "shown
    /// where applicable" instead of a code only ever being usable if the shopper already knew it
    /// existed. A Hidden code (the targeted, email-only campaign case) never appears here even
    /// though it still works if typed into <see cref="ApplyCoupon"/>/<see cref="ApplyPromotion"/>.
    /// </summary>
    [HttpGet("available-offers")]
    [AllowAnonymous]
    public async Task<ActionResult<List<AvailableOfferResponse>>> GetAvailableOffers([FromQuery] string? businessId, CancellationToken ct)
    {
        var result = await cartService.GetAvailableOffersAsync(ResolveBusinessId(businessId), ResolveOwner(), ct);
        return Ok(result);
    }

    /// <summary>
    /// §9.27. Call this right after a customer logs in or registers, passing the guest token they
    /// were holding, so the cart they built anonymously survives.
    /// </summary>
    [HttpPost("merge")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<CartResponse>> Merge([FromQuery] string guestToken, CancellationToken ct)
    {
        var result = await cartService.MergeGuestCartAsync(
            CurrentUser.TenantId, CurrentUser.BusinessId, CurrentUser.UserId, guestToken, ct);
        return Ok(result);
    }

    [HttpDelete]
    [AllowAnonymous]
    public async Task<IActionResult> Clear([FromQuery] string? businessId, CancellationToken ct)
    {
        await cartService.ClearAsync(ResolveBusinessId(businessId), ResolveOwner(), ct);
        return NoContent();
    }

    /// <summary>
    /// A signed-in customer's identity always wins over any token they happen to be sending — a
    /// stale guest token must never be able to redirect an authenticated write to another cart.
    /// </summary>
    private CartOwner ResolveOwner() =>
        string.IsNullOrEmpty(CurrentUser.UserId)
            ? CartOwner.ForGuest(Request.Headers[CartTokenHeader].ToString() is { Length: > 0 } token ? token : null)
            : CartOwner.ForCustomer(CurrentUser.UserId);

    /// <summary>A customer's Business comes from their JWT; a guest has to name it.</summary>
    private string ResolveBusinessId(string? fromQuery) =>
        !string.IsNullOrEmpty(CurrentUser.BusinessId)
            ? CurrentUser.BusinessId
            : fromQuery ?? throw new Application.Common.Exceptions.ConflictException(
                "businessId is required for a guest cart.");

    private string ResolveTenantId(string? fromQuery) =>
        !string.IsNullOrEmpty(CurrentUser.TenantId) ? CurrentUser.TenantId : fromQuery ?? string.Empty;
}
