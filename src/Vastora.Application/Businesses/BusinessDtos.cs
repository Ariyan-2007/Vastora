using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Businesses;

public record BusinessResponse(
    string Id,
    string TenantId,
    string Name,
    string Slug,
    string? CustomDomain,
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
