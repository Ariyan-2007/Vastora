using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Application.Privacy;

/// <summary>
/// Everything held about one customer, in one document — §9.37's "download my data". Orders are
/// included because they are the customer's own purchase history, not merely the merchant's records.
/// </summary>
public record CustomerDataExport(
    DateTime ExportedAt,
    object Profile,
    List<object> Addresses,
    List<object> Orders,
    List<object> Reviews,
    List<object> WishlistItems,
    List<object> StoreCredit,
    List<object> Returns);

public record UpdateNotificationPreferencesRequest(
    bool MarketingEmail,
    bool BackInStockAlerts,
    bool ReviewRequests,
    bool MarketingSms);

/// <summary>
/// §9.37. Data-subject rights, plus the tenant-offboarding side of the same problem: a subscriber
/// who cancels could previously neither get their data out nor have it removed.
/// </summary>
public interface IPrivacyService
{
    Task<CustomerDataExport> ExportCustomerDataAsync(string businessId, string customerUserId, CancellationToken ct = default);

    /// <summary>
    /// Right to erasure. PII is overwritten in place rather than the document deleted, because
    /// orders reference this user id and destroying it would orphan the merchant's own financial
    /// records — which the merchant has an independent legal duty to retain.
    /// </summary>
    Task AnonymizeCustomerAsync(string businessId, string customerUserId, CancellationToken ct = default);

    Task UpdatePreferencesAsync(string userId, UpdateNotificationPreferencesRequest request, CancellationToken ct = default);

    /// <summary>One-click unsubscribe by token — no login, because a marketing recipient may not have an account session.</summary>
    Task<bool> UnsubscribeByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>Everything a Tenant owns, for offboarding. Platform-only.</summary>
    Task<object> ExportTenantDataAsync(string tenantId, CancellationToken ct = default);
}

/// <inheritdoc cref="IPrivacyService"/>
public class PrivacyService(
    IMongoRepository<AppUser> users,
    IMongoRepository<Order> orders,
    IMongoRepository<Review> reviews,
    IMongoRepository<WishlistItem> wishlist,
    IMongoRepository<StoreCreditEntry> storeCredit,
    IMongoRepository<ReturnRequest> returns,
    IMongoRepository<Business> businesses,
    IMongoRepository<Product> products) : IPrivacyService
{
    public async Task<CustomerDataExport> ExportCustomerDataAsync(string businessId, string customerUserId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(customerUserId, ct)
            ?? throw new NotFoundException(nameof(AppUser), customerUserId);

        var userOrders = await orders.FindAsync(o => o.BusinessId == businessId && o.CustomerUserId == customerUserId, ct);
        var userReviews = await reviews.FindAsync(r => r.BusinessId == businessId && r.CustomerUserId == customerUserId, ct);
        var saved = await wishlist.FindAsync(w => w.BusinessId == businessId && w.CustomerUserId == customerUserId, ct);
        var credit = await storeCredit.FindAsync(c => c.BusinessId == businessId && c.CustomerUserId == customerUserId, ct);
        var rmas = await returns.FindAsync(r => r.BusinessId == businessId && r.CustomerUserId == customerUserId, ct);

        return new CustomerDataExport(
            DateTime.UtcNow,
            new
            {
                user.Id, user.FullName, user.Email, user.Phone,
                Role = user.Role.ToString(),
                Status = user.Status.ToString(),
                user.CreatedAt, user.LastLoginAt, user.EmailVerifiedAt,
                Preferences = user.NotificationPreferences
            },
            [.. user.Addresses.Cast<object>()],
            [.. userOrders.Select(o => (object)new
            {
                o.OrderNumber, o.PlacedAt, o.Total, o.Currency,
                Status = o.Status.ToString(),
                Items = o.Items.Select(i => new { i.ProductName, i.Quantity, i.UnitPrice })
            })],
            [.. userReviews.Select(r => (object)new { r.ProductId, r.Rating, r.Title, r.Body, r.CreatedAt })],
            [.. saved.Select(w => (object)new { w.ProductId, w.CreatedAt })],
            [.. credit.Select(c => (object)new { c.Amount, Reason = c.Reason.ToString(), c.Note, c.CreatedAt })],
            [.. rmas.Select(r => (object)new { r.RmaNumber, r.OrderNumber, Status = r.Status.ToString(), r.CreatedAt })]);
    }

    public async Task AnonymizeCustomerAsync(string businessId, string customerUserId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(customerUserId, ct)
            ?? throw new NotFoundException(nameof(AppUser), customerUserId);

        if (user.AnonymizedAt is not null)
        {
            return;
        }

        // The email must stay unique and must stay non-null (it is the login key), so it is
        // replaced with an unusable placeholder rather than blanked.
        user.FullName = "Deleted customer";
        user.Email = $"anonymised-{user.Id}@deleted.invalid";
        user.Phone = string.Empty;
        user.Addresses.Clear();
        user.PasswordHash = string.Empty;
        user.Status = Domain.Enums.UserStatus.Blocked;
        user.AnonymizedAt = DateTime.UtcNow;
        user.NotificationPreferences = new NotificationPreferences
        {
            TransactionalEmail = false,
            MarketingEmail = false,
            BackInStockAlerts = false,
            ReviewRequests = false
        };

        await users.UpdateAsync(user, ct);

        // Reviews carry a display name, so they need scrubbing separately — the review text itself
        // is the merchant's published content and stays.
        var userReviews = await reviews.FindAsync(r => r.CustomerUserId == customerUserId, ct);
        foreach (var review in userReviews)
        {
            review.CustomerName = "Deleted customer";
            await reviews.UpdateAsync(review, ct);
        }

        // Orders keep their financial substance but lose the contact details.
        var userOrders = await orders.FindAsync(o => o.CustomerUserId == customerUserId, ct);
        foreach (var order in userOrders)
        {
            order.ContactEmail = string.Empty;
            order.ContactPhone = string.Empty;
            order.ShippingAddress = null;
            order.BillingAddress = null;
            await orders.UpdateAsync(order, ct);
        }

        var saved = await wishlist.FindAsync(w => w.CustomerUserId == customerUserId, ct);
        foreach (var item in saved)
        {
            await wishlist.HardDeleteAsync(item.Id, ct);
        }
    }

    public async Task UpdatePreferencesAsync(string userId, UpdateNotificationPreferencesRequest request, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        var previouslyOptedIn = user.NotificationPreferences.MarketingEmail;

        user.NotificationPreferences.MarketingEmail = request.MarketingEmail;
        user.NotificationPreferences.BackInStockAlerts = request.BackInStockAlerts;
        user.NotificationPreferences.ReviewRequests = request.ReviewRequests;
        user.NotificationPreferences.MarketingSms = request.MarketingSms;

        // Consent timestamp records when opt-in happened, which is the thing a regulator asks for.
        // Only stamped on the transition into consent, never refreshed by an unrelated edit.
        if (request.MarketingEmail && !previouslyOptedIn)
        {
            user.NotificationPreferences.MarketingConsentAt = DateTime.UtcNow;
        }

        await users.UpdateAsync(user, ct);
    }

    public async Task<bool> UnsubscribeByTokenAsync(string token, CancellationToken ct = default)
    {
        var user = await users.FindOneAsync(u => u.UnsubscribeToken == token, ct);
        if (user is null)
        {
            return false;
        }

        user.NotificationPreferences.MarketingEmail = false;
        user.NotificationPreferences.MarketingSms = false;
        user.NotificationPreferences.ReviewRequests = false;
        await users.UpdateAsync(user, ct);

        return true;
    }

    public async Task<object> ExportTenantDataAsync(string tenantId, CancellationToken ct = default)
    {
        var tenantBusinesses = await businesses.FindAsync(b => b.TenantId == tenantId, ct);
        var tenantProducts = await products.FindAsync(p => p.TenantId == tenantId, ct);
        var tenantOrders = await orders.FindAsync(o => o.TenantId == tenantId, ct);
        var staff = await users.FindAsync(u => u.TenantId == tenantId, ct);

        return new
        {
            ExportedAt = DateTime.UtcNow,
            TenantId = tenantId,
            Businesses = tenantBusinesses,
            Products = tenantProducts,
            Orders = tenantOrders,
            // Credentials are stripped even from the owner's own export — there is no legitimate
            // reason for a password hash to leave the system.
            Users = staff.Select(u => new { u.Id, u.FullName, u.Email, u.Phone, Role = u.Role.ToString(), u.CreatedAt })
        };
    }
}
