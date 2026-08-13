using System.Linq.Expressions;
using Vastora.Domain.Common;

namespace Vastora.Application.Common.Interfaces;

/// <summary>Thin abstraction over an IMongoCollection&lt;T&gt; so Application code never touches the driver directly.</summary>
public interface IMongoRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<List<T>> GetAllAsync(CancellationToken ct = default);

    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<long> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<T> AddAsync(T entity, CancellationToken ct = default);

    Task UpdateAsync(T entity, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}
