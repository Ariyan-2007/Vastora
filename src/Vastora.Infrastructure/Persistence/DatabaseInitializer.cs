using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Infrastructure.Persistence;

/// <summary>
/// Runs once at startup: creates the unique indexes that back our slug/code invariants,
/// and makes sure exactly one PlatformSuperAdmin exists so there's always a way into
/// the platform-level API on a fresh database.
/// </summary>
public class DatabaseInitializer(
    MongoDbContext context,
    IMongoRepository<AppUser> users,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await CreateIndexesAsync(ct);
        await SeedPlatformAdminAsync(ct);
    }

    private async Task CreateIndexesAsync(CancellationToken ct)
    {
        var tenants = context.GetCollection<TenantAccount>();
        await tenants.Indexes.CreateOneAsync(
            new CreateIndexModel<TenantAccount>(Builders<TenantAccount>.IndexKeys.Ascending(t => t.Slug),
                new CreateIndexOptions { Unique = true }), cancellationToken: ct);

        var businesses = context.GetCollection<Business>();
        await businesses.Indexes.CreateOneAsync(
            new CreateIndexModel<Business>(Builders<Business>.IndexKeys.Ascending(b => b.Slug),
                new CreateIndexOptions { Unique = true }), cancellationToken: ct);

        var categories = context.GetCollection<Category>();
        await categories.Indexes.CreateOneAsync(
            new CreateIndexModel<Category>(
                Builders<Category>.IndexKeys.Ascending(c => c.BusinessId).Ascending(c => c.Slug),
                new CreateIndexOptions { Unique = true }), cancellationToken: ct);

        var products = context.GetCollection<Product>();
        await products.Indexes.CreateOneAsync(
            new CreateIndexModel<Product>(
                Builders<Product>.IndexKeys.Ascending(p => p.BusinessId).Ascending(p => p.Slug),
                new CreateIndexOptions { Unique = true }), cancellationToken: ct);

        var coupons = context.GetCollection<Coupon>();
        await coupons.Indexes.CreateOneAsync(
            new CreateIndexModel<Coupon>(
                Builders<Coupon>.IndexKeys.Ascending(c => c.BusinessId).Ascending(c => c.Code),
                new CreateIndexOptions { Unique = true }), cancellationToken: ct);

        var refreshTokens = context.GetCollection<RefreshToken>();
        await refreshTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<RefreshToken>(Builders<RefreshToken>.IndexKeys.Ascending(t => t.TokenHash)),
            cancellationToken: ct);
    }

    private async Task SeedPlatformAdminAsync(CancellationToken ct)
    {
        var alreadyExists = await users.ExistsAsync(u => u.Role == UserRole.PlatformSuperAdmin, ct);
        if (alreadyExists)
        {
            return;
        }

        var email = configuration["PlatformAdmin:Email"] ?? "admin@vastora.dev";
        var configuredPassword = configuration["PlatformAdmin:Password"];
        var generatedPassword = string.IsNullOrWhiteSpace(configuredPassword);
        var password = generatedPassword ? Guid.NewGuid().ToString("N")[..16] : configuredPassword!;

        var admin = new AppUser
        {
            FullName = "Vastora Platform Admin",
            Email = email,
            PasswordHash = passwordHasher.Hash(password),
            Role = UserRole.PlatformSuperAdmin,
            Status = UserStatus.Active
        };
        await users.AddAsync(admin, ct);

        logger.LogWarning(
            "Seeded PlatformSuperAdmin account. Email: {Email}{PasswordNote}",
            email,
            generatedPassword ? $" | Generated password: {password} (set PlatformAdmin:Password / PLATFORMADMIN__PASSWORD to control this)" : " | Password taken from configuration.");
    }
}
