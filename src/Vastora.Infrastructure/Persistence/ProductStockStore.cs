using MongoDB.Driver;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Infrastructure.Persistence;

/// <inheritdoc cref="IProductStockStore"/>
public class ProductStockStore(MongoDbContext context) : IProductStockStore
{
    private readonly IMongoCollection<Product> _products = context.GetCollection<Product>();

    private static readonly FilterDefinition<Product> NotDeleted =
        Builders<Product>.Filter.Ne(p => p.IsDeleted, true);

    public async Task<StockAdjustResult> TryAdjustAsync(
        string productId,
        string? variantId,
        int delta,
        CancellationToken ct = default)
    {
        return variantId is null
            ? await AdjustProductAsync(productId, delta, ct)
            : await AdjustVariantAsync(productId, variantId, delta, ct);
    }

    private async Task<StockAdjustResult> AdjustProductAsync(string productId, int delta, CancellationToken ct)
    {
        // "Result >= 0" expressed as a pre-condition on the stored value, since that is all the
        // database can filter on: current >= -delta. For a positive delta this is trivially true.
        var filter = Builders<Product>.Filter.And(
            NotDeleted,
            Builders<Product>.Filter.Eq(p => p.Id, productId),
            Builders<Product>.Filter.Gte(p => p.StockQuantity, -delta));

        var update = Builders<Product>.Update
            .Inc(p => p.StockQuantity, delta)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        var updated = await _products.FindOneAndUpdateAsync(
            filter, update,
            new FindOneAndUpdateOptions<Product> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is not null)
        {
            return new StockAdjustResult(true, updated.StockQuantity, updated.StockQuantity);
        }

        // Distinguish "not enough stock" from "no such product" for the caller's error message.
        var current = await _products.Find(Builders<Product>.Filter.And(
            NotDeleted, Builders<Product>.Filter.Eq(p => p.Id, productId))).FirstOrDefaultAsync(ct);

        return new StockAdjustResult(false, 0, current?.StockQuantity ?? 0);
    }

    private async Task<StockAdjustResult> AdjustVariantAsync(string productId, string variantId, int delta, CancellationToken ct)
    {
        // ElemMatch both identifies the variant and enforces the guard in one predicate, so the
        // positional $ operator below updates exactly the element that satisfied both.
        var filter = Builders<Product>.Filter.And(
            NotDeleted,
            Builders<Product>.Filter.Eq(p => p.Id, productId),
            Builders<Product>.Filter.ElemMatch(
                p => p.Variants,
                v => v.Id == variantId && v.StockQuantity >= -delta));

        var update = Builders<Product>.Update
            .Inc("Variants.$.StockQuantity", delta)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        var updated = await _products.FindOneAndUpdateAsync(
            filter, update,
            new FindOneAndUpdateOptions<Product> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is not null)
        {
            var variant = updated.Variants.FirstOrDefault(v => v.Id == variantId);
            return new StockAdjustResult(true, variant?.StockQuantity ?? 0, variant?.StockQuantity ?? 0);
        }

        var current = await _products.Find(Builders<Product>.Filter.And(
            NotDeleted, Builders<Product>.Filter.Eq(p => p.Id, productId))).FirstOrDefaultAsync(ct);
        var available = current?.Variants.FirstOrDefault(v => v.Id == variantId)?.StockQuantity ?? 0;

        return new StockAdjustResult(false, 0, available);
    }

    public async Task SetReviewAggregateAsync(string productId, double averageRating, int reviewCount, CancellationToken ct = default)
    {
        var update = Builders<Product>.Update
            .Set(p => p.AverageRating, averageRating)
            .Set(p => p.ReviewCount, reviewCount)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        await _products.UpdateOneAsync(
            Builders<Product>.Filter.And(NotDeleted, Builders<Product>.Filter.Eq(p => p.Id, productId)),
            update, cancellationToken: ct);
    }

    public async Task<Product?> GetIfInStockAsync(string productId, CancellationToken ct = default) =>
        await _products.Find(Builders<Product>.Filter.And(
            NotDeleted,
            Builders<Product>.Filter.Eq(p => p.Id, productId),
            Builders<Product>.Filter.Gt(p => p.StockQuantity, 0))).FirstOrDefaultAsync(ct);
}
