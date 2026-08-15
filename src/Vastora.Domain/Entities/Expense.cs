using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>Manually entered cost that doesn't come from an Order (rent, ads, wages) — §9.16b.</summary>
public class Expense : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTime IncurredAt { get; set; } = DateTime.UtcNow;

    public string CreatedByUserId { get; set; } = string.Empty;
}
