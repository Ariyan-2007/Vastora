namespace Vastora.Application.Cart;

public interface ICartService
{
    Task<CartResponse> GetAsync(string businessId, string customerUserId, CancellationToken ct = default);

    Task<CartResponse> AddItemAsync(string tenantId, string businessId, string customerUserId, AddCartItemRequest request, CancellationToken ct = default);

    Task<CartResponse> UpdateItemAsync(string businessId, string customerUserId, string productId, UpdateCartItemRequest request, CancellationToken ct = default);

    Task<CartResponse> RemoveItemAsync(string businessId, string customerUserId, string productId, CancellationToken ct = default);

    Task<CartResponse> ApplyCouponAsync(string businessId, string customerUserId, ApplyCartCouponRequest request, CancellationToken ct = default);

    Task ClearAsync(string businessId, string customerUserId, CancellationToken ct = default);
}
