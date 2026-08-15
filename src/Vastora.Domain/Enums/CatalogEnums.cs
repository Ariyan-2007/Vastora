namespace Vastora.Domain.Enums;

public enum ProductStatus
{
    Draft = 1,
    Active = 2,
    OutOfStock = 3,
    Archived = 4
}

public enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2
}

/// <summary>§9.15 — every write to Product.StockQuantity is logged as one of these.</summary>
public enum StockMovementType
{
    Sale = 1,
    Restock = 2,
    Return = 3,
    Adjustment = 4,
    DamageWriteOff = 5
}
