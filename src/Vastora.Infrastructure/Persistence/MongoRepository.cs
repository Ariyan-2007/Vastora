using System.Linq.Expressions;
using MongoDB.Driver;
using Vastora.Application.Common;
using AppSortDirection = Vastora.Application.Common.SortDirection;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Common;

namespace Vastora.Infrastructure.Persistence;

public class MongoRepository<T> : IMongoRepository<T> where T : BaseEntity
{
    private readonly IMongoCollection<T> _collection;

    /// <summary>
    /// §9.35. ANDed into every read so soft-deleted documents are invisible platform-wide without
    /// a single Application-layer caller remembering to ask. Written as "not true" rather than
    /// "== false" deliberately: documents written before IsDeleted existed have no such field at
    /// all, and `Ne(true)` matches a missing field where `Eq(false)` would not.
    /// </summary>
    private static readonly FilterDefinition<T> NotDeleted =
        Builders<T>.Filter.Ne(e => e.IsDeleted, true);

    public MongoRepository(MongoDbContext context)
    {
        _collection = context.GetCollection<T>();
    }

    private static FilterDefinition<T> Live(Expression<Func<T, bool>> predicate) =>
        Builders<T>.Filter.And(NotDeleted, Builders<T>.Filter.Where(predicate));

    private static FilterDefinition<T> LiveById(string id) =>
        Builders<T>.Filter.And(NotDeleted, Builders<T>.Filter.Eq(e => e.Id, id));

    public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _collection.Find(LiveById(id)).FirstOrDefaultAsync(ct);

    public async Task<List<T>> GetAllAsync(CancellationToken ct = default) =>
        await _collection.Find(NotDeleted).ToListAsync(ct);

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await _collection.Find(Live(predicate)).ToListAsync(ct);

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await _collection.Find(Live(predicate)).FirstOrDefaultAsync(ct);

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await _collection.Find(Live(predicate)).AnyAsync(ct);

    public async Task<long> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await _collection.CountDocumentsAsync(Live(predicate), cancellationToken: ct);

    public async Task<PagedResult<T>> FindPagedAsync(
        Expression<Func<T, bool>> predicate,
        PageRequest page,
        Expression<Func<T, object?>>? sortBy = null,
        AppSortDirection direction = AppSortDirection.Descending,
        CancellationToken ct = default)
    {
        var filter = Live(predicate);

        // Two round trips rather than one $facet aggregation: the count is what makes a pager
        // renderable, and at these page sizes the second query is cheaper than the pipeline.
        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        if (total == 0)
        {
            return PagedResult<T>.Empty(page);
        }

        var sortField = sortBy ?? (e => e.CreatedAt);
        var sort = direction == AppSortDirection.Ascending
            ? Builders<T>.Sort.Ascending(sortField)
            : Builders<T>.Sort.Descending(sortField);

        var items = await _collection
            .Find(filter)
            .Sort(sort)
            .Skip(page.Skip)
            .Limit(page.PageSize)
            .ToListAsync(ct);

        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }

    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        entity.CreatedAt = DateTime.UtcNow;
        await _collection.InsertOneAsync(entity, cancellationToken: ct);
        return entity;
    }

    public async Task AddManyAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var list = entities.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var entity in list)
        {
            entity.CreatedAt = now;
        }

        await _collection.InsertManyAsync(list, cancellationToken: ct);
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        await _collection.ReplaceOneAsync(e => e.Id == entity.Id, entity, cancellationToken: ct);
    }

    public async Task DeleteAsync(string id, string? deletedByUserId = null, CancellationToken ct = default)
    {
        var update = Builders<T>.Update
            .Set(e => e.IsDeleted, true)
            .Set(e => e.DeletedAt, DateTime.UtcNow)
            .Set(e => e.DeletedByUserId, deletedByUserId)
            .Set(e => e.UpdatedAt, DateTime.UtcNow);

        await _collection.UpdateOneAsync(LiveById(id), update, cancellationToken: ct);
    }

    public async Task<long> DeleteManyAsync(Expression<Func<T, bool>> predicate, string? deletedByUserId = null, CancellationToken ct = default)
    {
        var update = Builders<T>.Update
            .Set(e => e.IsDeleted, true)
            .Set(e => e.DeletedAt, DateTime.UtcNow)
            .Set(e => e.DeletedByUserId, deletedByUserId)
            .Set(e => e.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateManyAsync(Live(predicate), update, cancellationToken: ct);
        return result.ModifiedCount;
    }

    public async Task HardDeleteAsync(string id, CancellationToken ct = default) =>
        await _collection.DeleteOneAsync(e => e.Id == id, ct);

    public async Task<long> HardDeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        var result = await _collection.DeleteManyAsync(predicate, ct);
        return result.DeletedCount;
    }

    public async Task<T?> TryAdjustIntAsync(
        string id,
        Expression<Func<T, int>> field,
        int delta,
        int minResultValue = 0,
        CancellationToken ct = default)
    {
        // The guard is expressed as a pre-condition on the *current* value, because MongoDB can
        // only filter on what is already stored: "result >= min" is "current >= min - delta".
        var filter = Builders<T>.Filter.And(
            LiveById(id),
            Builders<T>.Filter.Gte(field, minResultValue - delta));

        var update = Builders<T>.Update
            .Inc(field, delta)
            .Set(e => e.UpdatedAt, DateTime.UtcNow);

        return await _collection.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<T> { ReturnDocument = ReturnDocument.After },
            ct);
    }

    public async Task<T?> TryIncrementBelowAsync(
        string id,
        Expression<Func<T, int>> field,
        int exclusiveMax,
        CancellationToken ct = default)
    {
        var filter = Builders<T>.Filter.And(
            LiveById(id),
            Builders<T>.Filter.Lt(field, exclusiveMax));

        var update = Builders<T>.Update
            .Inc(field, 1)
            .Set(e => e.UpdatedAt, DateTime.UtcNow);

        return await _collection.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<T> { ReturnDocument = ReturnDocument.After },
            ct);
    }

    public async Task<T?> IncrementAsync(string id, Expression<Func<T, int>> field, int delta, CancellationToken ct = default)
    {
        var update = Builders<T>.Update
            .Inc(field, delta)
            .Set(e => e.UpdatedAt, DateTime.UtcNow);

        return await _collection.FindOneAndUpdateAsync(
            LiveById(id),
            update,
            new FindOneAndUpdateOptions<T> { ReturnDocument = ReturnDocument.After },
            ct);
    }

    public async Task<long?> NextSequenceAsync(string id, Expression<Func<T, long>> field, CancellationToken ct = default)
    {
        var update = Builders<T>.Update
            .Inc(field, 1L)
            .Set(e => e.UpdatedAt, DateTime.UtcNow);

        var updated = await _collection.FindOneAndUpdateAsync(
            LiveById(id),
            update,
            new FindOneAndUpdateOptions<T> { ReturnDocument = ReturnDocument.After },
            ct);

        return updated is null ? null : field.Compile()(updated);
    }
}
