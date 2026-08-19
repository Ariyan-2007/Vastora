using Vastora.Application.Businesses;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Coupons;
using Vastora.Application.Notifications;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Promotions;

/// <summary>
/// §9.43. Who to email a code to: an explicit list of customers, a whole <c>CustomerGroup</c>
/// segment, or both — the two sets are unioned, not required together.
/// </summary>
public record SendDiscountEmailRequest(string Code, List<string>? CustomerUserIds, string? CustomerGroupId);

public record SendDiscountEmailResult(int TotalRecipients, int Sent, int SkippedOptedOut);

/// <summary>
/// §9.43. The delivery half of a Hidden discount code: a code with
/// <see cref="DiscountVisibility.Hidden"/> never appears in the storefront's available-offers
/// listing, so the only way a customer learns it exists is being told directly. This is that
/// "told directly" — it works for a Public code too (nothing stops a merchant emailing a
/// discoverable code as well), but it is what makes a Hidden one usable at all.
/// </summary>
public interface IDiscountEmailService
{
    Task<SendDiscountEmailResult> SendAsync(string tenantId, string businessId, SendDiscountEmailRequest request, CancellationToken ct = default);
}

/// <inheritdoc cref="IDiscountEmailService"/>
public class DiscountEmailService(
    IMongoRepository<Coupon> coupons,
    IMongoRepository<Promotion> promotions,
    IMongoRepository<CustomerGroup> customerGroups,
    IMongoRepository<AppUser> users,
    IMongoRepository<Business> businesses,
    INotificationService notificationService,
    IPlatformSettings platformSettings) : IDiscountEmailService
{
    public async Task<SendDiscountEmailResult> SendAsync(
        string tenantId, string businessId, SendDiscountEmailRequest request, CancellationToken ct = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var (label, description, expiresAt) = await ResolveCodeAsync(businessId, code, ct);

        var recipientIds = new HashSet<string>(request.CustomerUserIds ?? []);
        if (!string.IsNullOrWhiteSpace(request.CustomerGroupId))
        {
            var group = await customerGroups.GetByIdAsync(request.CustomerGroupId, ct);
            if (group is null || group.TenantId != tenantId || group.BusinessId != businessId)
            {
                throw new NotFoundException(nameof(CustomerGroup), request.CustomerGroupId);
            }

            recipientIds.UnionWith(group.CustomerUserIds);
        }

        if (recipientIds.Count == 0)
        {
            throw new ConflictException("Name at least one customer or a customer group to send to.");
        }

        var business = BusinessAssetUrls.ResolveLogo(await businesses.GetByIdAsync(businessId, ct), platformSettings.ApiBaseUrl);
        var sent = 0;
        var skipped = 0;

        foreach (var userId in recipientIds)
        {
            var user = await users.GetByIdAsync(userId, ct);
            if (user is null || user.BusinessId != businessId)
            {
                continue;
            }

            // A targeted discount email is marketing mail, not a transactional receipt — it
            // respects the same opt-out every other marketing send does (§9.36), including a
            // Hidden code: "the customer can only find out by email" is not license to email
            // someone who has opted out.
            if (!user.NotificationPreferences.MarketingEmail)
            {
                skipped++;
                continue;
            }

            var unsubscribeUrl = string.IsNullOrEmpty(user.UnsubscribeToken)
                ? null
                : $"/unsubscribe/{user.UnsubscribeToken}";

            var (subject, plainBody, htmlBody) = EmailTemplates.DiscountCode(
                business, user.FullName, code, label, description, expiresAt, unsubscribeUrl);

            await notificationService.NotifyAsync(new NotificationMessage(user.Email, subject, plainBody, htmlBody, BusinessId: businessId), ct);
            sent++;
        }

        return new SendDiscountEmailResult(recipientIds.Count, sent, skipped);
    }

    private async Task<(string Label, string Description, DateTime? ExpiresAt)> ResolveCodeAsync(
        string businessId, string code, CancellationToken ct)
    {
        var coupon = await coupons.FindOneAsync(c => c.BusinessId == businessId && c.Code == code, ct);
        if (coupon is not null)
        {
            var description = coupon.DiscountType == DiscountType.Percentage
                ? $"{coupon.DiscountValue:0.##}% off"
                : $"{coupon.DiscountValue:0.00} off";
            return (coupon.Code, description, coupon.ExpiresAt);
        }

        var promotion = await promotions.FindOneAsync(p => p.BusinessId == businessId && p.Code == code, ct);
        if (promotion is not null)
        {
            return (promotion.Name, promotion.Name, promotion.EndsAt);
        }

        throw new NotFoundException("Coupon or Promotion", code);
    }
}
