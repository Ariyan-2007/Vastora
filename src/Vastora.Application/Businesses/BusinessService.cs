using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Tenants;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Businesses;

public class BusinessService(
    IMongoRepository<Business> businesses,
    IMongoRepository<TenantAccount> tenants) : IBusinessService
{
    public async Task<BusinessResponse> CreateAsync(string tenantId, CreateBusinessRequest request, CancellationToken ct = default)
    {
        var tenant = await tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(TenantAccount), tenantId);

        var existingCount = await businesses.CountAsync(b => b.TenantId == tenantId, ct);
        if (tenant.Type == TenantType.SingleBusiness && existingCount >= 1)
        {
            throw new ConflictException(
                "This tenant is subscribed as a single-business account and already owns a Business. Upgrade to a multi-business plan to add another.");
        }

        var limits = SubscriptionPlanLimits.For(tenant.Plan);
        if (limits.MaxBusinesses is int maxBusinesses && existingCount >= maxBusinesses)
        {
            throw new ConflictException(
                $"Your '{tenant.Plan}' plan allows up to {maxBusinesses} Business(es). Upgrade your plan to add another.");
        }

        var slug = await GenerateUniqueSlugAsync(request.Slug ?? request.Name, ct);

        var business = new Business
        {
            TenantId = tenantId,
            Name = request.Name,
            Slug = slug,
            Description = request.Description,
            ContactEmail = request.ContactEmail,
            ContactPhone = request.ContactPhone,
            Currency = request.Currency,
            Status = BusinessStatus.Active
        };

        await businesses.AddAsync(business, ct);
        return Map(business);
    }

    public async Task<BusinessResponse> GetByIdAsync(string tenantId, string businessId, CancellationToken ct = default)
    {
        var business = await GetScopedAsync(tenantId, businessId, ct);
        return Map(business);
    }

    public async Task<BusinessResponse> GetByIdForPlatformAsync(string businessId, CancellationToken ct = default)
    {
        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);
        return Map(business);
    }

    public async Task<BusinessResponse> GetPublicBySlugAsync(string slug, CancellationToken ct = default)
    {
        var business = await businesses.FindOneAsync(b => b.Slug == slug && b.Status == BusinessStatus.Active, ct)
            ?? throw new NotFoundException(nameof(Business), slug);
        return Map(business);
    }

    public async Task<List<BusinessResponse>> GetAllForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var list = await businesses.FindAsync(b => b.TenantId == tenantId, ct);
        return list.Select(Map).ToList();
    }

    public async Task<BusinessResponse> UpdateAsync(string tenantId, string businessId, UpdateBusinessRequest request, CancellationToken ct = default)
    {
        var business = await GetScopedAsync(tenantId, businessId, ct);

        business.Name = request.Name;
        business.Description = request.Description;
        business.LogoUrl = request.LogoUrl;
        business.BannerUrl = request.BannerUrl;
        business.ThemeColor = request.ThemeColor;
        business.ContactEmail = request.ContactEmail;
        business.ContactPhone = request.ContactPhone;
        business.Currency = request.Currency;
        business.DefaultDeliveryFee = request.DefaultDeliveryFee;

        // §9B settings are patch-style: a caller that omits a section leaves it as it was, so the
        // pre-§9B request body keeps working and a partial update can't silently reset tax config.
        if (request.Tax is not null)
        {
            business.Tax = request.Tax;
        }

        if (request.Invoicing is not null)
        {
            // LastNumber is the invoice sequence counter (§9.33) and must never be settable from
            // a request — rewinding it would issue duplicate invoice numbers.
            business.Invoicing = request.Invoicing with { LastNumber = business.Invoicing.LastNumber };
        }

        if (request.ReturnWindowDays is { } returnWindow)
        {
            business.ReturnWindowDays = returnWindow;
        }

        if (request.ReviewsEnabled is { } reviewsEnabled)
        {
            business.ReviewsEnabled = reviewsEnabled;
        }

        if (request.AutoPublishReviews is { } autoPublish)
        {
            business.AutoPublishReviews = autoPublish;
        }

        if (request.GuestCheckoutEnabled is { } guestCheckout)
        {
            business.GuestCheckoutEnabled = guestCheckout;
        }

        await businesses.UpdateAsync(business, ct);
        return Map(business);
    }

    public async Task<BusinessResponse> SetLogoAsync(string tenantId, string businessId, string logoUrl, CancellationToken ct = default)
    {
        var business = await GetScopedAsync(tenantId, businessId, ct);
        business.LogoUrl = logoUrl;
        business.UpdatedAt = DateTime.UtcNow;
        await businesses.UpdateAsync(business, ct);
        return Map(business);
    }

    public async Task<BusinessResponse> SetBannerAsync(string tenantId, string businessId, string bannerUrl, CancellationToken ct = default)
    {
        var business = await GetScopedAsync(tenantId, businessId, ct);
        business.BannerUrl = bannerUrl;
        business.UpdatedAt = DateTime.UtcNow;
        await businesses.UpdateAsync(business, ct);
        return Map(business);
    }

    public async Task<BusinessResponse> UpdateStatusAsync(string tenantId, string businessId, BusinessStatus status, CancellationToken ct = default)
    {
        var business = await GetScopedAsync(tenantId, businessId, ct);
        business.Status = status;
        business.UpdatedAt = DateTime.UtcNow;
        await businesses.UpdateAsync(business, ct);
        return Map(business);
    }

    public async Task<BusinessResponse> UpdateDeliveryModuleAsync(string tenantId, string businessId, bool enabled, CancellationToken ct = default)
    {
        var business = await GetScopedAsync(tenantId, businessId, ct);
        business.DeliveryModuleEnabled = enabled;
        business.UpdatedAt = DateTime.UtcNow;
        await businesses.UpdateAsync(business, ct);
        return Map(business);
    }

    public async Task<BusinessMailSettingsResponse> GetMailSettingsAsync(string tenantId, string businessId, CancellationToken ct = default)
    {
        var business = await GetScopedAsync(tenantId, businessId, ct);
        return MapMailSettings(business.MailSettings);
    }

    public async Task<BusinessMailSettingsResponse> UpdateMailSettingsAsync(string tenantId, string businessId, UpdateBusinessMailSettingsRequest request, CancellationToken ct = default)
    {
        var business = await GetScopedAsync(tenantId, businessId, ct);

        business.MailSettings.Enabled = request.Enabled;
        business.MailSettings.Host = request.Host;
        business.MailSettings.Port = request.Port;
        business.MailSettings.Username = request.Username;
        business.MailSettings.FromAddress = request.FromAddress;
        business.MailSettings.FromName = request.FromName;
        if (!string.IsNullOrEmpty(request.Password))
        {
            business.MailSettings.Password = request.Password;
        }

        business.UpdatedAt = DateTime.UtcNow;
        await businesses.UpdateAsync(business, ct);
        return MapMailSettings(business.MailSettings);
    }

    public async Task<BusinessResponse> UpdateDomainsAsync(string tenantId, string businessId, UpdateBusinessDomainsRequest request, CancellationToken ct = default)
    {
        var business = await GetScopedAsync(tenantId, businessId, ct);
        business.ShopDomain = string.IsNullOrWhiteSpace(request.ShopDomain) ? null : request.ShopDomain.Trim();
        business.BackOfficeDomain = string.IsNullOrWhiteSpace(request.BackOfficeDomain) ? null : request.BackOfficeDomain.Trim();
        business.UpdatedAt = DateTime.UtcNow;
        await businesses.UpdateAsync(business, ct);
        return Map(business);
    }

    private static BusinessMailSettingsResponse MapMailSettings(BusinessMailSettings s) => new(
        s.Enabled, s.Host, s.Port, s.Username, !string.IsNullOrEmpty(s.Password), s.FromAddress, s.FromName);

    private async Task<Business> GetScopedAsync(string tenantId, string businessId, CancellationToken ct)
    {
        var business = await businesses.GetByIdAsync(businessId, ct);
        if (business is null || business.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(Business), businessId);
        }

        return business;
    }

    private async Task<string> GenerateUniqueSlugAsync(string seed, CancellationToken ct)
    {
        var baseSlug = SlugHelper.Slugify(seed);
        var attempt = 0;
        while (true)
        {
            var candidate = SlugHelper.WithSuffix(baseSlug, attempt);
            var taken = await businesses.ExistsAsync(b => b.Slug == candidate, ct);
            if (!taken)
            {
                return candidate;
            }

            attempt++;
        }
    }

    private static BusinessResponse Map(Business b) => new(
        b.Id, b.TenantId, b.Name, b.Slug, b.ShopDomain, b.BackOfficeDomain, b.Description, b.LogoUrl, b.BannerUrl,
        b.ThemeColor, b.Currency, b.ContactEmail, b.ContactPhone, b.Status, b.DeliveryModuleEnabled,
        b.DefaultDeliveryFee, b.CreatedAt,
        b.Tax, b.ReturnWindowDays, b.ReviewsEnabled, b.GuestCheckoutEnabled);
}
