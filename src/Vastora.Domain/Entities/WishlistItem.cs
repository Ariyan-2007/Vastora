using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>
/// One saved product per customer per business — §9.26. A row-per-item rather than an embedded
/// list on AppUser so back-in-stock alerts (§9.36) can query "who wants this product?" directly
/// instead of scanning every user document.
/// </summary>
public class WishlistItem : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string CustomerUserId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    /// <summary>Set once a back-in-stock alert has gone out, so the customer isn't notified twice for one restock.</summary>
    public DateTime? BackInStockNotifiedAt { get; set; }
}
