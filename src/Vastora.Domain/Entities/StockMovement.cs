using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>
/// One audited change to a Product's StockQuantity — §9.15a. Every write to StockQuantity
/// (checkout, order cancellation, manual adjustment) goes through IInventoryService.RecordMovementAsync,
/// which writes one of these alongside the field update, instead of any caller touching
/// StockQuantity directly. QuantityDelta is signed: positive adds to stock, negative removes.
/// </summary>
public class StockMovement : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    /// <summary>§9.22. Set when the movement was against a specific variant's stock rather than the product's own.</summary>
    public string? VariantId { get; set; }

    public StockMovementType Type { get; set; }

    public int QuantityDelta { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? ReferenceOrderId { get; set; }

    public string? CreatedByUserId { get; set; }
}
