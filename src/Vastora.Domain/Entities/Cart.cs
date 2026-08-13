using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

public class CartItem
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

/// <summary>One active cart per Customer per Business.</summary>
public class Cart : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string CustomerUserId { get; set; } = string.Empty;

    public List<CartItem> Items { get; set; } = [];

    public string? CouponCode { get; set; }
}
