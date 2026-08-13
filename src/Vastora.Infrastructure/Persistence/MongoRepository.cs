using System.Linq.Expressions;
using MongoDB.Driver;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Common;

namespace Vastora.Infrastructure.Persistence;

public class MongoRepository<T> : IMongoRepository<T> where T : BaseEntity
{
    private readonly IMongoCollection<T> _collection;

    public MongoRepository(MongoDbContext context)
    {
        _collection = context.GetCollection<T>();
    }

    public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _collection.Find(e => e.Id == id).FirstOrDefaultAsync(ct);

    public async Task<List<T>> GetAllAsync(CancellationToken ct = default) =>
        await _collection.Find(FilterDefinition<T>.Empty).ToListAsync(ct);

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await _collection.Find(predicate).ToListAsync(ct);

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await _collection.Find(predicate).FirstOrDefaultAsync(ct);

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await _collection.Find(predicate).AnyAsync(ct);

    public async Task<long> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await _collection.CountDocumentsAsync(predicate, cancellationToken: ct);

    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        entity.CreatedAt = DateTime.UtcNow;
        await _collection.InsertOneAsync(entity, cancellationToken: ct);
        return entity;
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default) =>
        await _collection.ReplaceOneAsync(e => e.Id == entity.Id, entity, cancellationToken: ct);

    public async Task DeleteAsync(string id, CancellationToken ct = default) =>
        await _collection.DeleteOneAsync(e => e.Id == id, ct);
}
