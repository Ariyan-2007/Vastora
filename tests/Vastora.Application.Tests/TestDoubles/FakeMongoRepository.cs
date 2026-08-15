using System.Linq.Expressions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Common;

namespace Vastora.Application.Tests.TestDoubles;

/// <summary>
/// In-memory IMongoRepository&lt;T&gt; for tests. Compiles and evaluates the predicate against a
/// real backing list — a Moq setup returning canned data for "any predicate" can't exercise
/// actual filtering logic (business/staff/product-count limits, order lookups, etc.), so this
/// is a hand-written fake rather than a mock.
/// </summary>
public class FakeMongoRepository<T> : IMongoRepository<T> where T : BaseEntity
{
    private readonly List<T> _items = [];
    private int _idCounter = 1;

    public List<T> Seed(params T[] entities)
    {
        foreach (var entity in entities)
        {
            if (string.IsNullOrEmpty(entity.Id))
            {
                entity.Id = (_idCounter++).ToString();
            }

            _items.Add(entity);
        }

        return _items;
    }

    public Task<T?> GetByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_items.FirstOrDefault(e => e.Id == id));

    public Task<List<T>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(_items.ToList());

    public Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Task.FromResult(_items.Where(predicate.Compile()).ToList());

    public Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Task.FromResult(_items.Where(predicate.Compile()).FirstOrDefault());

    public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Task.FromResult(_items.Any(predicate.Compile()));

    public Task<long> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Task.FromResult((long)_items.Count(predicate.Compile()));

    public Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(entity.Id))
        {
            entity.Id = (_idCounter++).ToString();
        }

        entity.CreatedAt = DateTime.UtcNow;
        _items.Add(entity);
        return Task.FromResult(entity);
    }

    public Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        var index = _items.FindIndex(e => e.Id == entity.Id);
        if (index >= 0)
        {
            _items[index] = entity;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _items.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }
}
