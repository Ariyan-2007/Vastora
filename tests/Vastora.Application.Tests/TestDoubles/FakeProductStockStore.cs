using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Application.Tests.TestDoubles;

/// <summary>
/// In-memory <see cref="IProductStockStore"/> backed by the same FakeMongoRepository the service
/// under test uses, so a stock change made here is visible through that repository.
///
/// The real implementation's value is the *database-side* guard, which no in-memory double can
/// reproduce. What this does reproduce exactly is the contract the calling services branch on:
/// <c>Applied = false</c> when a negative delta would overdraw, and the resulting balance when it
/// wouldn't. That is what the checkout-rollback and oversell tests actually exercise.
/// </summary>
public class FakeProductStockStore(FakeMongoRepository<Product> products) : IProductStockStore
{
    public async Task<StockAdjustResult> TryAdjustAsync(string productId, string? variantId, int delta, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(productId, ct);
        if (product is null)
        {
            return new StockAdjustResult(false, 0, 0);
        }

        if (variantId is null)
        {
            if (product.StockQuantity + delta < 0)
            {
                return new StockAdjustResult(false, 0, product.StockQuantity);
            }

            product.StockQuantity += delta;
            await products.UpdateAsync(product, ct);
            return new StockAdjustResult(true, product.StockQuantity, product.StockQuantity);
        }

        var variant = product.Variants.FirstOrDefault(v => v.Id == variantId);
        if (variant is null || variant.StockQuantity + delta < 0)
        {
            return new StockAdjustResult(false, 0, variant?.StockQuantity ?? 0);
        }

        variant.StockQuantity += delta;
        await products.UpdateAsync(product, ct);
        return new StockAdjustResult(true, variant.StockQuantity, variant.StockQuantity);
    }

    public async Task SetReviewAggregateAsync(string productId, double averageRating, int reviewCount, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(productId, ct);
        if (product is null)
        {
            return;
        }

        product.AverageRating = averageRating;
        product.ReviewCount = reviewCount;
        await products.UpdateAsync(product, ct);
    }

    public async Task<Product?> GetIfInStockAsync(string productId, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(productId, ct);
        return product is { StockQuantity: > 0 } ? product : null;
    }
}
