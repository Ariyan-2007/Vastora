using Vastora.Domain.Entities;

namespace Vastora.Infrastructure.Persistence;

internal static class CollectionNames
{
    private static readonly Dictionary<Type, string> Map = new()
    {
        [typeof(TenantAccount)] = "tenants",
        [typeof(Business)] = "businesses",
        [typeof(AppUser)] = "users",
        [typeof(RefreshToken)] = "refreshTokens",
        [typeof(Category)] = "categories",
        [typeof(Product)] = "products",
        [typeof(Coupon)] = "coupons",
        [typeof(Cart)] = "carts",
        [typeof(Order)] = "orders",
        [typeof(DeliveryAgentProfile)] = "deliveryAgentProfiles"
    };

    public static string For<T>() => Map.TryGetValue(typeof(T), out var name) ? name : $"{typeof(T).Name}s";
}
