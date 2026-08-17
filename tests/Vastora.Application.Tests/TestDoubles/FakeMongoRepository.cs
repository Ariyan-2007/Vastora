using System.Linq.Expressions;
using Vastora.Application.Common;
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

    /// <summary>
    /// Mirrors MongoRepository's global soft-delete filter (§9.35) — flagged rows stay in the
    /// backing list but no read returns them, so tests see the same visibility production does.
    /// </summary>
    private IEnumerable<T> Live => _items.Where(e => !e.IsDeleted);

    public Task<T?> GetByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Live.FirstOrDefault(e => e.Id == id));

    public Task<List<T>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(Live.ToList());

    public Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Task.FromResult(Live.Where(predicate.Compile()).ToList());

    public Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Task.FromResult(Live.Where(predicate.Compile()).FirstOrDefault());

    public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Task.FromResult(Live.Any(predicate.Compile()));

    public Task<long> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Task.FromResult((long)Live.Count(predicate.Compile()));

    public Task<PagedResult<T>> FindPagedAsync(
        Expression<Func<T, bool>> predicate,
        PageRequest page,
        Expression<Func<T, object?>>? sortBy = null,
        SortDirection direction = SortDirection.Descending,
        CancellationToken ct = default)
    {
        var matched = Live.Where(predicate.Compile()).ToList();

        var key = sortBy?.Compile() ?? (e => e.CreatedAt);
        matched = direction == SortDirection.Ascending
            ? [.. matched.OrderBy(key)]
            : [.. matched.OrderByDescending(key)];

        var items = matched.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult(new PagedResult<T>(items, page.Page, page.PageSize, matched.Count));
    }

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

    public Task AddManyAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        foreach (var entity in entities)
        {
            AddAsync(entity, ct);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, string? deletedByUserId = null, CancellationToken ct = default)
    {
        var entity = _items.FirstOrDefault(e => e.Id == id);
        if (entity is not null)
        {
            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.UtcNow;
            entity.DeletedByUserId = deletedByUserId;
        }

        return Task.CompletedTask;
    }

    public Task<long> DeleteManyAsync(Expression<Func<T, bool>> predicate, string? deletedByUserId = null, CancellationToken ct = default)
    {
        var matched = Live.Where(predicate.Compile()).ToList();
        foreach (var entity in matched)
        {
            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.UtcNow;
            entity.DeletedByUserId = deletedByUserId;
        }

        return Task.FromResult((long)matched.Count);
    }

    public Task HardDeleteAsync(string id, CancellationToken ct = default)
    {
        _items.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }

    public Task<long> HardDeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        var removed = _items.RemoveAll(new Predicate<T>(predicate.Compile()));
        return Task.FromResult((long)removed);
    }

    // The atomic helpers below are single-threaded here by construction — the point of the real
    // implementations is the database-side guard, which an in-memory list cannot reproduce. What
    // these do reproduce faithfully is the *contract*: null when the guard would have failed, the
    // updated entity when it would have passed. That is what the calling services branch on.

    public Task<T?> TryAdjustIntAsync(
        string id,
        Expression<Func<T, int>> field,
        int delta,
        int minResultValue = 0,
        CancellationToken ct = default)
    {
        var entity = Live.FirstOrDefault(e => e.Id == id);
        if (entity is null)
        {
            return Task.FromResult<T?>(null);
        }

        var (target, property) = ResolveProperty(entity, field);
        var current = (int)property.GetValue(target)!;
        if (current + delta < minResultValue)
        {
            return Task.FromResult<T?>(null);
        }

        property.SetValue(target, current + delta);
        entity.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult<T?>(entity);
    }

    public Task<T?> TryIncrementBelowAsync(
        string id,
        Expression<Func<T, int>> field,
        int exclusiveMax,
        CancellationToken ct = default)
    {
        var entity = Live.FirstOrDefault(e => e.Id == id);
        if (entity is null)
        {
            return Task.FromResult<T?>(null);
        }

        var (target, property) = ResolveProperty(entity, field);
        var current = (int)property.GetValue(target)!;
        if (current >= exclusiveMax)
        {
            return Task.FromResult<T?>(null);
        }

        property.SetValue(target, current + 1);
        entity.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult<T?>(entity);
    }

    public Task<T?> IncrementAsync(string id, Expression<Func<T, int>> field, int delta, CancellationToken ct = default)
    {
        var entity = Live.FirstOrDefault(e => e.Id == id);
        if (entity is null)
        {
            return Task.FromResult<T?>(null);
        }

        var (target, property) = ResolveProperty(entity, field);
        property.SetValue(target, (int)property.GetValue(target)! + delta);
        entity.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult<T?>(entity);
    }

    public Task<long?> NextSequenceAsync(string id, Expression<Func<T, long>> field, CancellationToken ct = default)
    {
        var entity = Live.FirstOrDefault(e => e.Id == id);
        if (entity is null)
        {
            return Task.FromResult<long?>(null);
        }

        var (target, property) = ResolveProperty(entity, field);
        var next = (long)property.GetValue(target)! + 1;
        property.SetValue(target, next);
        entity.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult<long?>(next);
    }

    /// <summary>
    /// Resolves a member expression to the property *and the object it lives on*, so a nested
    /// target like <c>b => b.Invoicing.LastNumber</c> is read and written against the
    /// InvoiceSettings instance rather than the root entity (which would throw).
    /// </summary>
    private static (object Target, System.Reflection.PropertyInfo Property) ResolveProperty<TField>(
        T entity,
        Expression<Func<T, TField>> selector)
    {
        if (selector.Body is not MemberExpression member || member.Member is not System.Reflection.PropertyInfo property)
        {
            throw new InvalidOperationException($"Expected a property access, got: {selector.Body}");
        }

        // member.Expression is the parameter itself for a direct property, or a further member
        // access for a nested one — compiling it yields the instance that actually owns the property.
        var ownerExpression = member.Expression
            ?? throw new InvalidOperationException($"Expected an instance property, got: {selector.Body}");

        if (ownerExpression is ParameterExpression)
        {
            return (entity, property);
        }

        var owner = Expression.Lambda(ownerExpression, selector.Parameters).Compile().DynamicInvoke(entity)
            ?? throw new InvalidOperationException($"Owner of {property.Name} was null on {typeof(T).Name}.");

        return (owner, property);
    }
}
