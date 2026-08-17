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
        [typeof(DeliveryAgentProfile)] = "deliveryAgentProfiles",
        [typeof(PasswordResetToken)] = "passwordResetTokens",
        [typeof(StockMovement)] = "stockMovements",
        [typeof(LedgerEntry)] = "ledgerEntries",
        [typeof(Expense)] = "expenses",
        [typeof(Review)] = "reviews",
        [typeof(WishlistItem)] = "wishlistItems",
        [typeof(ReturnRequest)] = "returnRequests",
        [typeof(EmailVerificationToken)] = "emailVerificationTokens",
        [typeof(IdempotencyRecord)] = "idempotencyRecords",
        [typeof(AuditLogEntry)] = "auditLog",
        [typeof(ContentBlock)] = "contentBlocks",
        [typeof(GiftCard)] = "giftCards",
        [typeof(StoreCreditEntry)] = "storeCreditEntries",
        [typeof(CustomerGroup)] = "customerGroups",
        [typeof(Promotion)] = "promotions",
        [typeof(ShippingZone)] = "shippingZones",
        [typeof(WebhookSubscription)] = "webhookSubscriptions",
        [typeof(WebhookDelivery)] = "webhookDeliveries",
        [typeof(ApiKey)] = "apiKeys"
    };

    public static string For<T>() => Map.TryGetValue(typeof(T), out var name) ? name : $"{typeof(T).Name}s";
}
