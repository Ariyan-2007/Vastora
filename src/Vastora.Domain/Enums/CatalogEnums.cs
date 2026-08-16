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

/// <summary>§9.25. Reviews are held for moderation by default — see ReviewService.</summary>
public enum ReviewStatus
{
    Pending = 1,
    Published = 2,
    Rejected = 3
}

/// <summary>Public catalog sort options — §9.29.</summary>
public enum ProductSort
{
    /// <summary>IsFeatured first, then SortWeight ascending, then newest. The merchandising default.</summary>
    Relevance = 1,
    Newest = 2,
    PriceAscending = 3,
    PriceDescending = 4,
    TopRated = 5,
    BestSelling = 6,
    NameAscending = 7
}
