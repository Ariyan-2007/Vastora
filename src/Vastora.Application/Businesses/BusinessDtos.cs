using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Businesses;

public record BusinessResponse(
    string Id,
    string TenantId,
    string Name,
    string Slug,
    string? ShopDomain,
    string? BackOfficeDomain,
    string Description,
    string LogoUrl,
    string BannerUrl,
    string ThemeColor,
    string Currency,
    string ContactEmail,
    string ContactPhone,
    BusinessStatus Status,
    bool DeliveryModuleEnabled,
    decimal DefaultDeliveryFee,
    DateTime CreatedAt,
    // §9B additions. Storefronts read these to decide what to render — whether to show prices
    // as tax-inclusive, whether a returns or reviews UI belongs on the page at all, and whether
    // to offer guest checkout.
    TaxSettings Tax,
    int ReturnWindowDays,
    bool ReviewsEnabled,
    bool GuestCheckoutEnabled);

public record CreateBusinessRequest(
    string Name,
    string? Slug,
    string Description,
    string ContactEmail,
    string ContactPhone,
    string Currency = "USD");

/// <summary>
/// Every §9B field is optional with a null default, so the pre-§9B request body still works
/// unchanged: omitting a section leaves that setting exactly as it was rather than resetting it.
/// </summary>
public record UpdateBusinessRequest(
    string Name,
    string Description,
    string LogoUrl,
    string BannerUrl,
    string ThemeColor,
    string ContactEmail,
    string ContactPhone,
    string Currency,
    decimal DefaultDeliveryFee,
    /// <summary>§9.19. Omit to leave the current tax configuration alone.</summary>
    TaxSettings? Tax = null,
    /// <summary>§9.33 — the seller identity that must legally appear on an invoice.</summary>
    InvoiceSettings? Invoicing = null,
    /// <summary>§9.21. Zero disables returns entirely.</summary>
    int? ReturnWindowDays = null,
    bool? ReviewsEnabled = null,
    bool? AutoPublishReviews = null,
    /// <summary>§9.27.</summary>
    bool? GuestCheckoutEnabled = null);

public record UpdateDeliveryModuleRequest(bool Enabled);

/// <summary>
/// SuperOffice-only. Sets both domains a Business is reachable at — a real production domain, or a
/// dev tunnel (ngrok, etc.) while testing. Each is independently optional (send null/empty to clear
/// it); a caller resubmits both current values as a normal settings form, not a partial patch.
/// <see cref="ShopDomain"/> resolves <see cref="Business.LogoUrl"/>/<see cref="Business.BannerUrl"/>
/// to absolute URLs for outbound email (<c>BusinessAssetUrls</c>) and validates a Customer's own
/// redirectBaseUrl; <see cref="BackOfficeDomain"/> validates a BusinessAdmin/BusinessStaff/
/// DeliveryAgent's redirectBaseUrl. See <c>AuthService.ResolveLinkBase</c>.
/// </summary>
public record UpdateBusinessDomainsRequest(string? ShopDomain, string? BackOfficeDomain);

/// <summary>SuperOffice-only. Password is never echoed back — see <see cref="BusinessMailSettingsResponse"/>.</summary>
public record UpdateBusinessMailSettingsRequest(
    bool Enabled,
    string Host,
    int Port,
    string Username,
    /// <summary>Omit (null/empty) to leave the currently stored password unchanged — lets a caller edit Host/From without re-entering credentials.</summary>
    string? Password,
    string FromAddress,
    string FromName);

/// <summary><see cref="HasPassword"/> tells the SuperOffice UI whether a credential is already on file, without ever exposing it.</summary>
public record BusinessMailSettingsResponse(
    bool Enabled,
    string Host,
    int Port,
    string Username,
    bool HasPassword,
    string FromAddress,
    string FromName);

/// <summary>
/// Platform-only. <see cref="ConfirmSlug"/> must match the target Business's slug exactly — the
/// same "type the name to confirm" gate as GitHub repo deletion — so a wipe can't happen from a
/// stray click on the wrong row.
/// </summary>
public record WipeBusinessRequest(string ConfirmSlug);
