using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Analytics;

public class AnalyticsService(
    IMongoRepository<Business> businesses,
    IMongoRepository<Order> orders) : IAnalyticsService
{
    private const int TopProductCount = 10;

    public async Task<TenantAnalyticsResponse> GetTenantAnalyticsAsync(string tenantId, CancellationToken ct = default)
    {
        var tenantBusinesses = await businesses.FindAsync(b => b.TenantId == tenantId, ct);
        var allOrders = await orders.FindAsync(o => o.TenantId == tenantId, ct);

        var activeOrders = allOrders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
        // §9.47: Delivered and PickedUp both count as revenue-recognised — already materialised
        // above, so IsFulfilled() runs as plain LINQ-to-Objects rather than a Mongo query translation.
        var deliveredOrders = allOrders.Where(o => o.Status.IsFulfilled()).ToList();

        var businessEntries = tenantBusinesses
            .Select(b => new BusinessAnalyticsEntry(
                b.Id,
                b.Name,
                activeOrders.Count(o => o.BusinessId == b.Id),
                deliveredOrders.Where(o => o.BusinessId == b.Id).Sum(o => o.Total)))
            .ToList();

        var topProducts = deliveredOrders
            .SelectMany(o => o.Items)
            .GroupBy(i => (i.ProductId, i.ProductName))
            .Select(g => new TopProductStat(g.Key.ProductId, g.Key.ProductName, g.Sum(i => i.Quantity), g.Sum(i => i.LineTotal)))
            .OrderByDescending(p => p.QuantitySold)
            .Take(TopProductCount)
            .ToList();

        return new TenantAnalyticsResponse(
            deliveredOrders.Sum(o => o.Total),
            activeOrders.Count,
            businessEntries,
            topProducts);
    }
}
