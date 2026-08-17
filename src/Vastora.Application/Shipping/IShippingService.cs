using Vastora.Application.Common;
using Vastora.Domain.Entities;

namespace Vastora.Application.Shipping;

/// <summary>
/// §9.20. Replaces the single flat <c>Business.DefaultDeliveryFee</c> with destination- and
/// weight-aware rate tables, while keeping that flat fee as the fallback so nothing configured
/// before this existed changes behaviour.
/// </summary>
public interface IShippingService
{
    /// <summary>
    /// Every rate the customer may choose for this destination and basket. Empty when the
    /// Business has no zones configured — callers then fall back to <c>DefaultDeliveryFee</c>.
    /// </summary>
    Task<List<ShippingQuote>> GetQuotesAsync(
        string businessId,
        Address? destination,
        decimal orderSubtotal,
        decimal totalWeightKg,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the fee for a checkout. Precedence, most to least specific: an explicit
    /// caller-supplied fee, the named rate the customer selected, the cheapest matching rate,
    /// then the Business's flat default.
    /// </summary>
    Task<(decimal Fee, string? MethodName)> ResolveFeeAsync(
        Business business,
        Address? destination,
        decimal orderSubtotal,
        decimal totalWeightKg,
        string? selectedRateId,
        decimal? explicitFee,
        CancellationToken ct = default);

    Task<PagedResult<ShippingZoneResponse>> GetZonesAsync(string businessId, PageRequest page, CancellationToken ct = default);

    Task<ShippingZoneResponse> CreateZoneAsync(string tenantId, string businessId, CreateShippingZoneRequest request, CancellationToken ct = default);

    Task<ShippingZoneResponse> UpdateZoneAsync(string tenantId, string businessId, string zoneId, CreateShippingZoneRequest request, CancellationToken ct = default);

    Task DeleteZoneAsync(string tenantId, string businessId, string zoneId, CancellationToken ct = default);
}
