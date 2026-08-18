using Vastora.Application.Pricing;
using Vastora.Domain.Enums;

namespace Vastora.Application.Cart;

public interface ICartService
{
    Task<CartResponse> GetAsync(string businessId, CartOwner owner, CancellationToken ct = default);

    Task<CartResponse> AddItemAsync(string tenantId, string businessId, CartOwner owner, AddCartItemRequest request, CancellationToken ct = default);

    Task<CartResponse> UpdateItemAsync(string businessId, CartOwner owner, string productId, UpdateCartItemRequest request, CancellationToken ct = default);

    Task<CartResponse> RemoveItemAsync(string businessId, CartOwner owner, string productId, string? variantId, CancellationToken ct = default);

    Task<CartResponse> ApplyCouponAsync(string businessId, CartOwner owner, ApplyCartCouponRequest request, CancellationToken ct = default);

    Task<CartResponse> RemoveCouponAsync(string businessId, CartOwner owner, CancellationToken ct = default);

    /// <summary>§9.23. Promotion codes are a list — several can stack, unlike the single legacy coupon.</summary>
    Task<CartResponse> ApplyPromotionCodeAsync(string businessId, CartOwner owner, ApplyCartCouponRequest request, CancellationToken ct = default);

    Task<CartResponse> RemovePromotionCodeAsync(string businessId, CartOwner owner, string code, CancellationToken ct = default);

    /// <summary>§9.43. Validates the code against the gift card ledger before it sticks to the
    /// cart — an unknown or dead code fails here, where the shopper can see why, not silently at checkout.</summary>
    Task<CartResponse> ApplyGiftCardAsync(string businessId, CartOwner owner, ApplyCartCouponRequest request, CancellationToken ct = default);

    Task<CartResponse> RemoveGiftCardAsync(string businessId, CartOwner owner, string code, CancellationToken ct = default);

    /// <summary>§9.43. Opts this cart in (or out) of spending store credit — mirrors <c>CheckoutRequest.UseStoreCredit</c> so the preview and the charge agree.</summary>
    Task<CartResponse> SetUseStoreCreditAsync(string businessId, CartOwner owner, bool useStoreCredit, CancellationToken ct = default);

    /// <summary>§9.44. Sets how this cart previews delivery — Pickup/Digital drop the delivery fee
    /// and shipping options entirely. Preview-only, same relationship to
    /// <c>CheckoutRequest.FulfillmentMethod</c> that <see cref="SetUseStoreCreditAsync"/> has to
    /// <c>CheckoutRequest.UseStoreCredit</c>.</summary>
    Task<CartResponse> SetFulfillmentMethodAsync(string businessId, CartOwner owner, FulfillmentMethod fulfillmentMethod, CancellationToken ct = default);

    /// <summary>§9.43. Public coupon/promotion codes worth showing this shopper right now — see <see cref="IPricingService.GetAvailableOffersAsync"/>.</summary>
    Task<List<AvailableOfferResponse>> GetAvailableOffersAsync(string businessId, CartOwner owner, CancellationToken ct = default);

    Task ClearAsync(string businessId, CartOwner owner, CancellationToken ct = default);

    /// <summary>
    /// §9.27. Folds an anonymous cart into the customer's own on login or registration. Before
    /// guest carts existed there was nothing to merge, so a shopper who filled a cart and then
    /// signed in simply lost it.
    /// </summary>
    Task<CartResponse> MergeGuestCartAsync(string tenantId, string businessId, string customerUserId, string guestToken, CancellationToken ct = default);
}
