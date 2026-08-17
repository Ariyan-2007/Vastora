using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using CartEntity = Vastora.Domain.Entities.Cart;

namespace Vastora.Application.Businesses;

public class BusinessWipeService(
    IMongoRepository<Business> businesses,
    IMongoRepository<ApiKey> apiKeys,
    IMongoRepository<AppUser> appUsers,
    IMongoRepository<CartEntity> carts,
    IMongoRepository<Category> categories,
    IMongoRepository<ContentBlock> contentBlocks,
    IMongoRepository<Coupon> coupons,
    IMongoRepository<CustomerGroup> customerGroups,
    IMongoRepository<DeliveryAgentProfile> deliveryAgentProfiles,
    IMongoRepository<Expense> expenses,
    IMongoRepository<GiftCard> giftCards,
    IMongoRepository<LedgerEntry> ledgerEntries,
    IMongoRepository<Order> orders,
    IMongoRepository<Product> products,
    IMongoRepository<Promotion> promotions,
    IMongoRepository<ReturnRequest> returnRequests,
    IMongoRepository<Review> reviews,
    IMongoRepository<ShippingZone> shippingZones,
    IMongoRepository<StockMovement> stockMovements,
    IMongoRepository<StoreCreditEntry> storeCreditEntries,
    IMongoRepository<WebhookSubscription> webhookSubscriptions,
    IMongoRepository<WishlistItem> wishlistItems,
    IMongoRepository<RefreshToken> refreshTokens) : IBusinessWipeService
{
    public async Task WipeAsync(string tenantId, string businessId, string confirmSlug, string performedByUserId, CancellationToken ct = default)
    {
        var business = await businesses.GetByIdAsync(businessId, ct);
        if (business is null || business.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(Business), businessId);
        }

        if (!string.Equals(confirmSlug, business.Slug, StringComparison.Ordinal))
        {
            throw new ValidationAppException(new Dictionary<string, string[]>
            {
                ["confirmSlug"] = ["Must exactly match the Business's slug — nothing was deleted."]
            });
        }

        // Soft-delete the Business itself first so every read (storefront, BackOffice, Platform
        // console) stops surfacing it immediately, even if a step below fails partway through.
        await businesses.DeleteAsync(businessId, performedByUserId, ct);

        // Staff, delivery agents, and this Business's own shop customers — captured before the
        // bulk delete below so their sessions can be killed afterward.
        var businessUsers = await appUsers.FindAsync(u => u.BusinessId == businessId, ct);
        var businessUserIds = businessUsers.Select(u => u.Id).ToList();

        await apiKeys.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await appUsers.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await carts.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await categories.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await contentBlocks.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await coupons.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await customerGroups.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await deliveryAgentProfiles.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await expenses.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await giftCards.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await ledgerEntries.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await orders.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await products.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await promotions.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await returnRequests.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await reviews.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await shippingZones.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await stockMovements.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await storeCreditEntries.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await webhookSubscriptions.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);
        await wishlistItems.DeleteManyAsync(x => x.BusinessId == businessId, performedByUserId, ct);

        // AuditLogEntry is deliberately left alone — it's this Business's own paper trail,
        // including the record of this very wipe, and §9.35 exists so that trail survives.

        // Refresh tokens aren't business data (IMongoRepository's own carve-out for hard delete),
        // so kill every session this Business's users held outright rather than waiting for
        // natural JWT expiry.
        if (businessUserIds.Count > 0)
        {
            await refreshTokens.HardDeleteManyAsync(t => businessUserIds.Contains(t.UserId), ct);
        }
    }
}
