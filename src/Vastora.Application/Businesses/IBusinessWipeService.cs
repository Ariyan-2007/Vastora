namespace Vastora.Application.Businesses;

/// <summary>
/// Platform-only, effectively-irreversible removal of a Business and everything under it. Kept
/// separate from <see cref="IBusinessService"/> because it touches every Business-scoped
/// collection in the system rather than just Business itself — a different blast radius deserves
/// its own file rather than bloating the everyday service.
/// </summary>
public interface IBusinessWipeService
{
    /// <summary>
    /// Soft-deletes the Business and every document platform-wide scoped to it (the §9.35 pattern
    /// — nothing here is hard-deleted except session tokens, matching IMongoRepository's own
    /// guidance that hard delete is for tokens/idempotency records, not business data. That also
    /// means a wipe can, in principle, be undone directly against the database if it turns out to
    /// have been a mistake). <paramref name="confirmSlug"/> must match the Business's slug exactly
    /// or nothing is touched.
    /// </summary>
    Task WipeAsync(string tenantId, string businessId, string confirmSlug, string performedByUserId, CancellationToken ct = default);
}
