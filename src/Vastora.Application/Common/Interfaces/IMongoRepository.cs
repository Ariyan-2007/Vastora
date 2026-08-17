using System.Linq.Expressions;
using Vastora.Application.Common;
using Vastora.Domain.Common;

namespace Vastora.Application.Common.Interfaces;

/// <summary>
/// Thin abstraction over an IMongoCollection&lt;T&gt; so Application code never touches the driver
/// directly.
///
/// Two behaviours are worth knowing before using it:
/// <list type="bullet">
/// <item>Every read silently excludes soft-deleted documents (§9.35). Application code does not
/// filter on IsDeleted and must not — <c>DeleteAsync</c> flags, <c>HardDeleteAsync</c> removes.</item>
/// <item><c>UpdateAsync</c> replaces the whole document and therefore loses concurrent writes.
/// For counters that several requests race on — stock, coupon usage, invoice numbers — use the
/// atomic helpers below instead, which push the arithmetic into the database (§9.17).</item>
/// </list>
/// </summary>
public interface IMongoRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<List<T>> GetAllAsync(CancellationToken ct = default);

    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<long> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>
    /// §9.18. Skip/limit/sort and the total count evaluated by MongoDB, not by materialising the
    /// collection and sorting it in memory.
    /// </summary>
    Task<PagedResult<T>> FindPagedAsync(
        Expression<Func<T, bool>> predicate,
        PageRequest page,
        Expression<Func<T, object?>>? sortBy = null,
        SortDirection direction = SortDirection.Descending,
        CancellationToken ct = default);

    Task<T> AddAsync(T entity, CancellationToken ct = default);

    Task AddManyAsync(IEnumerable<T> entities, CancellationToken ct = default);

    Task UpdateAsync(T entity, CancellationToken ct = default);

    /// <summary>Soft delete — flags IsDeleted/DeletedAt and hides the document from every read.</summary>
    Task DeleteAsync(string id, string? deletedByUserId = null, CancellationToken ct = default);

    /// <summary>Soft delete for every document matching <paramref name="predicate"/> — bulk counterpart to <see cref="DeleteAsync"/>.</summary>
    Task<long> DeleteManyAsync(Expression<Func<T, bool>> predicate, string? deletedByUserId = null, CancellationToken ct = default);

    /// <summary>Genuinely removes the document. For expired tokens and idempotency records, not business data.</summary>
    Task HardDeleteAsync(string id, CancellationToken ct = default);

    Task<long> HardDeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>
    /// §9.17. Atomically applies <paramref name="delta"/> to an integer field, but only if the
    /// result would be at least <paramref name="minResultValue"/>. Returns the updated document,
    /// or null if the guard failed or the document is gone.
    ///
    /// This is what makes overselling impossible: the check and the decrement are one database
    /// operation, so two concurrent checkouts for the last unit cannot both pass. A read-then-write
    /// in C# cannot give that guarantee no matter how it is ordered.
    /// </summary>
    Task<T?> TryAdjustIntAsync(
        string id,
        Expression<Func<T, int>> field,
        int delta,
        int minResultValue = 0,
        CancellationToken ct = default);

    /// <summary>
    /// Same guarantee for a bounded counter: increments only while the field stays strictly below
    /// <paramref name="exclusiveMax"/>. Used for coupon/promotion usage caps, which were previously
    /// beatable by concurrent redemptions.
    /// </summary>
    Task<T?> TryIncrementBelowAsync(
        string id,
        Expression<Func<T, int>> field,
        int exclusiveMax,
        CancellationToken ct = default);

    /// <summary>Unconditional atomic increment, for counters with no cap (review counts, usage tallies).</summary>
    Task<T?> IncrementAsync(string id, Expression<Func<T, int>> field, int delta, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments a 64-bit counter and returns the value *after* the increment — the
    /// only safe way to hand out gapless sequential invoice numbers (§9.33) under concurrency.
    /// </summary>
    Task<long?> NextSequenceAsync(string id, Expression<Func<T, long>> field, CancellationToken ct = default);
}
