using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Vastora.Infrastructure.Persistence;

/// <summary>Backs the `/health` endpoint (§9.11) — a real ping against the configured MongoDB, not just "the process is up".</summary>
public class MongoHealthCheck(MongoDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext checkContext, CancellationToken ct = default)
    {
        try
        {
            await context.Database.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }", cancellationToken: ct);
            return HealthCheckResult.Healthy("MongoDB is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB is not reachable.", ex);
        }
    }
}
