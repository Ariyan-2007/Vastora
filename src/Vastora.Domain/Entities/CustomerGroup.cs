using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>
/// §9.23. A named segment of customers — wholesale, VIP, staff — that promotions and price
/// rules can target. Membership is a list of user ids on the group rather than a group id on
/// AppUser, so a customer can belong to several and a segment can be reshaped without touching
/// user documents.
/// </summary>
public class CustomerGroup : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> CustomerUserIds { get; set; } = [];

    /// <summary>
    /// Blanket percentage off the catalog for members — the price-list-lite of §9.23. A full
    /// per-product price list is still open; this covers the wholesale-tier case that motivates
    /// customer groups in the first place.
    /// </summary>
    public decimal? DiscountPercent { get; set; }

    public bool IsActive { get; set; } = true;
}
