using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Application.Shipping;

/// <inheritdoc cref="IShippingService"/>
public class ShippingService(IMongoRepository<ShippingZone> zones) : IShippingService
{
    public async Task<List<ShippingQuote>> GetQuotesAsync(
        string businessId,
        Address? destination,
        decimal orderSubtotal,
        decimal totalWeightKg,
        CancellationToken ct = default)
    {
        var all = await zones.FindAsync(z => z.BusinessId == businessId && z.IsActive, ct);
        if (all.Count == 0)
        {
            return [];
        }

        var zone = SelectZone(all, destination);
        if (zone is null)
        {
            return [];
        }

        return zone.Rates
            .Where(r => r.IsActive && Matches(r, orderSubtotal, totalWeightKg))
            .OrderBy(r => r.Price)
            .Select(r => new ShippingQuote(zone.Id, r.Id, r.Name, r.Price, r.EstimatedDaysMin, r.EstimatedDaysMax))
            .ToList();
    }

    public async Task<(decimal Fee, string? MethodName)> ResolveFeeAsync(
        Business business,
        Address? destination,
        decimal orderSubtotal,
        decimal totalWeightKg,
        string? selectedRateId,
        decimal? explicitFee,
        CancellationToken ct = default)
    {
        // An explicit fee wins outright — staff placing a phone order need to be able to override,
        // and the pre-§9.20 CheckoutRequest.DeliveryFee contract must keep working unchanged.
        if (explicitFee.HasValue)
        {
            return (explicitFee.Value, null);
        }

        var quotes = await GetQuotesAsync(business.Id, destination, orderSubtotal, totalWeightKg, ct);
        if (quotes.Count == 0)
        {
            return (business.DefaultDeliveryFee, null);
        }

        if (!string.IsNullOrWhiteSpace(selectedRateId))
        {
            var chosen = quotes.FirstOrDefault(q => q.RateId == selectedRateId)
                ?? throw new ConflictException("That shipping option isn't available for this address and basket.");
            return (chosen.Price, chosen.Name);
        }

        // Cheapest rather than first: never silently upsell a customer who didn't pick a method.
        var cheapest = quotes[0];
        return (cheapest.Price, cheapest.Name);
    }

    /// <summary>
    /// Most specific match wins: a zone naming both the country and the region beats one naming
    /// only the country, which beats the catch-all zone with no countries at all. Priority breaks
    /// remaining ties.
    /// </summary>
    private static ShippingZone? SelectZone(List<ShippingZone> candidates, Address? destination)
    {
        var country = destination?.Country?.Trim() ?? string.Empty;
        var region = destination?.State?.Trim() ?? string.Empty;

        return candidates
            .Select(z => new { Zone = z, Specificity = Specificity(z, country, region) })
            .Where(x => x.Specificity >= 0)
            .OrderByDescending(x => x.Specificity)
            .ThenBy(x => x.Zone.Priority)
            .Select(x => x.Zone)
            .FirstOrDefault();
    }

    /// <summary>-1 means "does not match at all"; higher is more specific.</summary>
    private static int Specificity(ShippingZone zone, string country, string region)
    {
        var countryMatches = zone.Countries.Count == 0
            || zone.Countries.Any(c => string.Equals(c, country, StringComparison.OrdinalIgnoreCase));

        if (!countryMatches)
        {
            return -1;
        }

        if (zone.Regions.Count > 0)
        {
            var regionMatches = zone.Regions.Any(r => string.Equals(r, region, StringComparison.OrdinalIgnoreCase));
            return regionMatches ? 2 : -1;
        }

        return zone.Countries.Count > 0 ? 1 : 0;
    }

    private static bool Matches(ShippingRate rate, decimal subtotal, decimal weightKg) =>
        (rate.MinOrderSubtotal is null || subtotal >= rate.MinOrderSubtotal)
        && (rate.MaxOrderSubtotal is null || subtotal < rate.MaxOrderSubtotal)
        && (rate.MinWeightKg is null || weightKg >= rate.MinWeightKg)
        && (rate.MaxWeightKg is null || weightKg < rate.MaxWeightKg);

    public async Task<PagedResult<ShippingZoneResponse>> GetZonesAsync(string businessId, PageRequest page, CancellationToken ct = default)
    {
        var result = await zones.FindPagedAsync(
            z => z.BusinessId == businessId, page, z => z.Priority, SortDirection.Ascending, ct);
        return result.Map(Map);
    }

    public async Task<ShippingZoneResponse> CreateZoneAsync(string tenantId, string businessId, CreateShippingZoneRequest request, CancellationToken ct = default)
    {
        var zone = new ShippingZone
        {
            TenantId = tenantId,
            BusinessId = businessId,
            Name = request.Name,
            Countries = request.Countries ?? [],
            Regions = request.Regions ?? [],
            Rates = BuildRates(request.Rates),
            Priority = request.Priority,
            IsActive = request.IsActive
        };

        await zones.AddAsync(zone, ct);
        return Map(zone);
    }

    public async Task<ShippingZoneResponse> UpdateZoneAsync(string tenantId, string businessId, string zoneId, CreateShippingZoneRequest request, CancellationToken ct = default)
    {
        var zone = await GetScopedAsync(tenantId, businessId, zoneId, ct);

        zone.Name = request.Name;
        zone.Countries = request.Countries ?? [];
        zone.Regions = request.Regions ?? [];
        zone.Rates = BuildRates(request.Rates);
        zone.Priority = request.Priority;
        zone.IsActive = request.IsActive;

        await zones.UpdateAsync(zone, ct);
        return Map(zone);
    }

    public async Task DeleteZoneAsync(string tenantId, string businessId, string zoneId, CancellationToken ct = default)
    {
        await GetScopedAsync(tenantId, businessId, zoneId, ct);
        await zones.DeleteAsync(zoneId, ct: ct);
    }

    private async Task<ShippingZone> GetScopedAsync(string tenantId, string businessId, string zoneId, CancellationToken ct)
    {
        var zone = await zones.GetByIdAsync(zoneId, ct);
        if (zone is null || zone.TenantId != tenantId || zone.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(ShippingZone), zoneId);
        }

        return zone;
    }

    /// <summary>Rate ids are server-generated, same rule as ProductVariant.Id — never trusted from the client.</summary>
    private static List<ShippingRate> BuildRates(List<ShippingRateRequest>? requests) =>
        (requests ?? []).Select(r => new ShippingRate
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = r.Name,
            Price = r.Price,
            MinOrderSubtotal = r.MinOrderSubtotal,
            MaxOrderSubtotal = r.MaxOrderSubtotal,
            MinWeightKg = r.MinWeightKg,
            MaxWeightKg = r.MaxWeightKg,
            EstimatedDaysMin = r.EstimatedDaysMin,
            EstimatedDaysMax = r.EstimatedDaysMax,
            IsActive = r.IsActive
        }).ToList();

    private static ShippingZoneResponse Map(ShippingZone z) => new(
        z.Id, z.Name, z.Countries, z.Regions,
        z.Rates.Select(r => new ShippingRateResponse(
            r.Id, r.Name, r.Price, r.MinOrderSubtotal, r.MaxOrderSubtotal,
            r.MinWeightKg, r.MaxWeightKg, r.EstimatedDaysMin, r.EstimatedDaysMax, r.IsActive)).ToList(),
        z.Priority, z.IsActive);
}
