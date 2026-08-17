using Vastora.Domain.Entities;

namespace Vastora.Application.Common.Interfaces;

/// <summary>Outcome of one guarded stock adjustment.</summary>
/// <param name="Applied">False when the guard refused the change — insufficient stock, or no such product/variant.</param>
/// <param name="NewQuantity">Stock after the change. Zero when nothing was applied.</param>
/// <param name="AvailableQuantity">What was actually on hand when the guard refused, for the error message.</param>
public record StockAdjustResult(bool Applied, int NewQuantity, int AvailableQuantity);

/// <summary>
/// §9.17 + §9.22. A purpose-built seam for the one operation the generic
/// <see cref="IMongoRepository{T}"/> cannot express: adjusting stock — at product level or inside
/// a specific embedded variant — atomically, with a "must not go below zero" guard evaluated by
/// the database rather than by C# between a read and a write.
///
/// This exists as its own interface rather than more methods on the repository because the
/// variant case needs a positional array update, which is Product-shaped knowledge that has no
/// business leaking into a generic <c>IMongoRepository&lt;T&gt;</c>.
/// </summary>
public interface IProductStockStore
{
    /// <summary>
    /// Applies <paramref name="delta"/> to stock. Negative deltas are refused if they would take
    /// the balance below zero — that refusal is what makes overselling impossible under
    /// concurrency. Positive deltas (restock, return) always apply.
    /// </summary>
    /// <param name="variantId">Null adjusts the product's own StockQuantity; otherwise the named variant's.</param>
    Task<StockAdjustResult> TryAdjustAsync(
        string productId,
        string? variantId,
        int delta,
        CancellationToken ct = default);

    /// <summary>
    /// Recomputes and stores a product's denormalised review aggregate (§9.25). Here rather than
    /// on a review repository because it writes to Product, and doing it in one atomic set avoids
    /// a read-modify-write race between two reviews landing at once.
    /// </summary>
    Task SetReviewAggregateAsync(string productId, double averageRating, int reviewCount, CancellationToken ct = default);

    /// <summary>Products whose stock crossed from zero to positive, for §9.36's back-in-stock alerts.</summary>
    Task<Product?> GetIfInStockAsync(string productId, CancellationToken ct = default);
}
