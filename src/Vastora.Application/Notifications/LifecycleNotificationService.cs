using Microsoft.Extensions.Logging;
using Vastora.Application.Businesses;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Webhooks;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Notifications;

/// <summary>What one sweep actually sent, so the worker's log line is meaningful.</summary>
public record LifecycleSweepResult(int AbandonedCartNudges, int BackInStockAlerts, int LowStockAlerts, int ReviewRequests);

/// <summary>
/// §9.36. The automated messages a storefront is expected to send and Vastora sent none of:
/// abandoned-cart recovery, back-in-stock alerts, merchant low-stock warnings, and post-delivery
/// review requests.
///
/// Every one of these is *marketing-adjacent* rather than transactional, so each respects
/// <c>AppUser.NotificationPreferences</c> — building the preference model before the sender was
/// deliberate: sending marketing mail with no opt-out is a legal problem, not a rude one.
/// </summary>
public interface ILifecycleNotificationService
{
    Task<LifecycleSweepResult> RunSweepAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="ILifecycleNotificationService"/>
public class LifecycleNotificationService(
    IMongoRepository<Domain.Entities.Cart> carts,
    IMongoRepository<AppUser> users,
    IMongoRepository<Product> products,
    IMongoRepository<Order> orders,
    IMongoRepository<Review> reviews,
    IMongoRepository<WishlistItem> wishlist,
    IMongoRepository<Business> businesses,
    INotificationService notificationService,
    IWebhookPublisher webhookPublisher,
    IPlatformSettings settings,
    ILogger<LifecycleNotificationService> logger) : ILifecycleNotificationService
{
    public async Task<LifecycleSweepResult> RunSweepAsync(CancellationToken ct = default)
    {
        var abandoned = await SweepAbandonedCartsAsync(ct);
        var backInStock = await SweepBackInStockAsync(ct);
        var lowStock = await SweepLowStockAsync(ct);
        var reviewRequests = await SweepReviewRequestsAsync(ct);

        return new LifecycleSweepResult(abandoned, backInStock, lowStock, reviewRequests);
    }

    /// <summary>
    /// The highest-ROI automated email in e-commerce, and it needed no new data: Cart already
    /// persisted with an UpdatedAt. One nudge per cart — AbandonedReminderSentAt is the guard,
    /// and checkout clears it so a returning shopper's next abandonment is eligible again.
    /// </summary>
    private async Task<int> SweepAbandonedCartsAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - settings.AbandonedCartAfter;
        var sent = 0;

        var stale = await carts.FindAsync(
            c => c.AbandonedReminderSentAt == null && c.UpdatedAt != null && c.UpdatedAt < cutoff, ct);

        foreach (var cart in stale.Where(c => c.Items.Count > 0))
        {
            var recipient = await ResolveMarketingRecipientAsync(cart.CustomerUserId, cart.ContactEmail, ct);
            if (!recipient.Allowed || string.IsNullOrWhiteSpace(recipient.Email))
            {
                // Still stamped, so an opted-out cart isn't re-examined on every sweep forever.
                cart.AbandonedReminderSentAt = DateTime.UtcNow;
                await carts.UpdateAsync(cart, ct);
                continue;
            }

            var itemNames = cart.Items.Take(3).Select(i => i.ProductName).ToList();
            var business = BusinessAssetUrls.ResolveLogo(await businesses.GetByIdAsync(cart.BusinessId, ct), settings.ApiBaseUrl);
            var unsubscribeUrl = UnsubscribeUrl(recipient.UnsubscribeToken);
            var (subject, plainBody, htmlBody) = EmailTemplates.AbandonedCart(
                business, recipient.Name, itemNames, cart.Items.Sum(i => i.Quantity), null, unsubscribeUrl);

            if (await TrySendAsync(recipient.Email, subject, plainBody, htmlBody, cart.BusinessId, ct))
            {
                sent++;
            }

            cart.AbandonedReminderSentAt = DateTime.UtcNow;
            await carts.UpdateAsync(cart, ct);
        }

        return sent;
    }

    /// <summary>Wishlist is what makes this possible — §9.26 is this feature's prerequisite.</summary>
    private async Task<int> SweepBackInStockAsync(CancellationToken ct)
    {
        var sent = 0;
        var pending = await wishlist.FindAsync(w => w.BackInStockNotifiedAt == null, ct);

        foreach (var entry in pending)
        {
            var product = await products.GetByIdAsync(entry.ProductId, ct);
            if (product is null || product.StockQuantity <= 0 || !product.IsPubliclyVisibleNow(DateTime.UtcNow))
            {
                continue;
            }

            var user = await users.GetByIdAsync(entry.CustomerUserId, ct);
            if (user is null || !user.NotificationPreferences.BackInStockAlerts)
            {
                continue;
            }

            var business = BusinessAssetUrls.ResolveLogo(await businesses.GetByIdAsync(product.BusinessId, ct), settings.ApiBaseUrl);
            var (subject, plainBody, htmlBody) = EmailTemplates.BackInStock(business, product.Name, null, UnsubscribeUrl(user.UnsubscribeToken));

            if (await TrySendAsync(user.Email, subject, plainBody, htmlBody, product.BusinessId, ct))
            {
                sent++;
            }

            entry.BackInStockNotifiedAt = DateTime.UtcNow;
            await wishlist.UpdateAsync(entry, ct);
        }

        return sent;
    }

    /// <summary>
    /// §9.15b left the low-stock signal queryable but never pushed it anywhere. This closes that
    /// loop — the merchant is told, rather than having to remember to look.
    /// </summary>
    private async Task<int> SweepLowStockAsync(CancellationToken ct)
    {
        var sent = 0;
        var tracked = await products.FindAsync(p => p.TrackInventory && p.ReorderThreshold != null, ct);

        foreach (var group in tracked.Where(p => p.StockQuantity <= p.ReorderThreshold!.Value).GroupBy(p => p.BusinessId))
        {
            var business = BusinessAssetUrls.ResolveLogo(await businesses.GetByIdAsync(group.Key, ct), settings.ApiBaseUrl);
            if (business is null || string.IsNullOrWhiteSpace(business.ContactEmail))
            {
                continue;
            }

            var items = group.Select(p => (p.Name, p.Sku, p.StockQuantity, p.ReorderThreshold)).ToList();
            var (subject, plainBody, htmlBody) = EmailTemplates.LowStockMerchant(business, items);

            if (await TrySendAsync(business.ContactEmail, subject, plainBody, htmlBody, business.Id, ct))
            {
                sent++;
            }

            foreach (var product in group)
            {
                await webhookPublisher.PublishAsync(product.TenantId, product.BusinessId, WebhookEvents.ProductLowStock,
                    new { productId = product.Id, sku = product.Sku, stockQuantity = product.StockQuantity, product.ReorderThreshold }, ct);
            }
        }

        return sent;
    }

    /// <summary>
    /// Asks for a review a few days after delivery — long enough for the customer to have formed
    /// an opinion. Skipped if they already reviewed, and one request per order, tracked by the
    /// absence of a review rather than a flag on the order.
    /// </summary>
    private async Task<int> SweepReviewRequestsAsync(CancellationToken ct)
    {
        const int daysAfterDelivery = 3;
        var sent = 0;

        var window = DateTime.UtcNow.AddDays(-daysAfterDelivery);
        // §9.47: written as a direct comparison rather than OrderStatus.IsFulfilled() — this
        // predicate is translated into a real MongoDB query by the driver's LINQ provider, which
        // (unlike the in-memory checks elsewhere) cannot execute an arbitrary C# extension method.
        var recentlyDelivered = await orders.FindPagedAsync(
            o => (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PickedUp) && o.PlacedAt < window,
            PageRequest.Of(1, PageRequest.MaxPageSize), o => o.PlacedAt, ct: ct);

        foreach (var order in recentlyDelivered.Items.Where(o => !string.IsNullOrEmpty(o.CustomerUserId)))
        {
            var user = await users.GetByIdAsync(order.CustomerUserId, ct);
            if (user is null || !user.NotificationPreferences.ReviewRequests)
            {
                continue;
            }

            var unreviewed = new List<string>();
            foreach (var item in order.Items)
            {
                var existing = await reviews.ExistsAsync(
                    r => r.BusinessId == order.BusinessId
                         && r.ProductId == item.ProductId
                         && r.CustomerUserId == order.CustomerUserId, ct);

                if (!existing)
                {
                    unreviewed.Add(item.ProductName);
                }
            }

            if (unreviewed.Count == 0)
            {
                continue;
            }

            var business = BusinessAssetUrls.ResolveLogo(await businesses.GetByIdAsync(order.BusinessId, ct), settings.ApiBaseUrl);
            var (subject, plainBody, htmlBody) = EmailTemplates.ReviewRequest(business, user.FullName, unreviewed, UnsubscribeUrl(user.UnsubscribeToken));

            if (await TrySendAsync(user.Email, subject, plainBody, htmlBody, order.BusinessId, ct))
            {
                sent++;
            }
        }

        return sent;
    }

    /// <summary>
    /// Resolves who to write to and whether they've agreed to hear from us. A guest cart has no
    /// AppUser and therefore no recorded consent — so it is treated as *not* opted in, which is
    /// the only defensible default.
    /// </summary>
    private async Task<(string? Email, bool Allowed, string? Name, string? UnsubscribeToken)> ResolveMarketingRecipientAsync(
        string customerUserId, string? fallbackEmail, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(customerUserId))
        {
            return (fallbackEmail, false, null, null);
        }

        var user = await users.GetByIdAsync(customerUserId, ct);
        return user is null
            ? (fallbackEmail, false, null, null)
            : (user.Email, user.NotificationPreferences.MarketingEmail, user.FullName, user.UnsubscribeToken);
    }

    /// <summary>Login-free opt-out link (§9.36) — the token alone identifies the account.</summary>
    private string UnsubscribeUrl(string? unsubscribeToken) =>
        string.IsNullOrEmpty(unsubscribeToken) ? string.Empty : $"{settings.PublicBaseUrl.TrimEnd('/')}/unsubscribe/{unsubscribeToken}";

    private async Task<bool> TrySendAsync(string email, string subject, string plainBody, string htmlBody, string? businessId, CancellationToken ct)
    {
        try
        {
            await notificationService.NotifyAsync(new NotificationMessage(email, subject, plainBody, htmlBody, BusinessId: businessId), ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lifecycle notification '{Subject}' to {Email} failed", subject, email);
            return false;
        }
    }
}
