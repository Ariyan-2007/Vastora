namespace Vastora.Application.Cart;

public interface ICartService
{
    Task<CartResponse> GetAsync(string businessId, CartOwner owner, CancellationToken ct = default);

    Task<CartResponse> AddItemAsync(string tenantId, string businessId, CartOwner owner, AddCartItemRequest request, CancellationToken ct = default);

    Task<CartResponse> UpdateItemAsync(string businessId, CartOwner owner, string productId, UpdateCartItemRequest request, CancellationToken ct = default);

    Task<CartResponse> RemoveItemAsync(string businessId, CartOwner owner, string productId, string? variantId, CancellationToken ct = default);

    Task<CartResponse> ApplyCouponAsync(string businessId, CartOwner owner, ApplyCartCouponRequest request, CancellationToken ct = default);

    /// <summary>§9.23. Promotion codes are a list — several can stack, unlike the single legacy coupon.</summary>
    Task<CartResponse> ApplyPromotionCodeAsync(string businessId, CartOwner owner, ApplyCartCouponRequest request, CancellationToken ct = default);

    Task<CartResponse> RemovePromotionCodeAsync(string businessId, CartOwner owner, string code, CancellationToken ct = default);

    Task ClearAsync(string businessId, CartOwner owner, CancellationToken ct = default);

    /// <summary>
    /// §9.27. Folds an anonymous cart into the customer's own on login or registration. Before
    /// guest carts existed there was nothing to merge, so a shopper who filled a cart and then
    /// signed in simply lost it.
    /// </summary>
    Task<CartResponse> MergeGuestCartAsync(string tenantId, string businessId, string customerUserId, string guestToken, CancellationToken ct = default);
}
