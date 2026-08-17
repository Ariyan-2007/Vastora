using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>
/// One storefront (Landing Page + Shop + BackOffice) owned by a Tenant.
/// A SingleBusiness tenant has exactly one of these; a MultiBusiness tenant has several,
/// all visible together through that tenant's SuperOffice.
/// </summary>
public class Business : BaseEntity, ITenantScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? CustomDomain { get; set; }

    public string Description { get; set; } = string.Empty;

    public string LogoUrl { get; set; } = string.Empty;

    public string BannerUrl { get; set; } = string.Empty;

    public string ThemeColor { get; set; } = "#111827";

    public string Currency { get; set; } = "USD";

    public string Timezone { get; set; } = "UTC";

    public string ContactEmail { get; set; } = string.Empty;

    public string ContactPhone { get; set; } = string.Empty;

    public Address? Address { get; set; }

    public BusinessStatus Status { get; set; } = BusinessStatus.Draft;

    /// <summary>
    /// Whether this Business uses the DeliveryAgent workflow at all. Pickup-only sellers or
    /// ones using a third-party courier can turn it off; new DeliveryAgent staff can't be
    /// created and orders can't be assigned to one while disabled, but existing agents and
    /// in-flight assignments are left alone.
    /// </summary>
    public bool DeliveryModuleEnabled { get; set; } = true;

    /// <summary>
    /// Flat delivery fee used at checkout when no ShippingZone matches — §9.7. Still the
    /// fallback, but §9.20's zones take precedence when any are configured.
    /// </summary>
    public decimal DefaultDeliveryFee { get; set; }

    /// <summary>§9.19.</summary>
    public TaxSettings Tax { get; set; } = new();

    /// <summary>§9.33 — the seller identity that must legally appear on an invoice.</summary>
    public InvoiceSettings Invoicing { get; set; } = new();

    /// <summary>§9.21. Days after delivery a customer may still open a return. Zero disables returns entirely.</summary>
    public int ReturnWindowDays { get; set; } = 14;

    /// <summary>§9.25. When false, the storefront exposes no review endpoints for this Business at all.</summary>
    public bool ReviewsEnabled { get; set; } = true;

    /// <summary>§9.25. When false, published reviews still require an Admin to approve each one.</summary>
    public bool AutoPublishReviews { get; set; }

    /// <summary>§9.27. Some sellers require accounts; this makes that a per-Business choice rather than a platform rule.</summary>
    public bool GuestCheckoutEnabled { get; set; } = true;
}

/// <summary>§9.19. Deliberately a single rate plus named overrides, not a jurisdiction engine.</summary>
public class TaxSettings
{
    public bool Enabled { get; set; }

    /// <summary>Percentage, e.g. 15 for 15% VAT.</summary>
    public decimal DefaultRatePercent { get; set; }

    /// <summary>
    /// True when catalog prices already contain tax (the VAT convention). The order's TaxAmount
    /// is then extracted from the total rather than added on top — getting this backwards
    /// overcharges every customer, so it is snapshotted onto each Order.
    /// </summary>
    public bool PricesIncludeTax { get; set; }

    /// <summary>Whether the delivery fee is taxed at the same rate. Jurisdiction-dependent.</summary>
    public bool TaxShipping { get; set; }

    /// <summary>Product.TaxClass → rate percent. A class with no entry falls back to DefaultRatePercent.</summary>
    public Dictionary<string, decimal> ClassRates { get; set; } = [];

    /// <summary>Shown on invoices — VAT/GST/BIN registration number.</summary>
    public string RegistrationNumber { get; set; } = string.Empty;

    /// <summary>What to call it on the invoice: "VAT", "GST", "Sales Tax".</summary>
    public string DisplayName { get; set; } = "Tax";
}

/// <summary>§9.33.</summary>
public record InvoiceSettings
{
    /// <summary>Prepended to the sequential number, e.g. "INV-". Changing it does not reset the sequence.</summary>
    public string NumberPrefix { get; set; } = "INV-";

    /// <summary>Last number issued. Incremented atomically so invoice numbers stay gapless and unique.</summary>
    public long LastNumber { get; set; }

    public string LegalName { get; set; } = string.Empty;

    public string LegalAddress { get; set; } = string.Empty;

    /// <summary>Company registration / trade licence number.</summary>
    public string RegistrationNumber { get; set; } = string.Empty;

    public string FooterNote { get; set; } = string.Empty;
}
