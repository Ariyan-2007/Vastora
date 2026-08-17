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

        await CreateQueryPathIndexesAsync(ct);
    }

    /// <summary>
    /// §9.18. Everything above is a *constraint* index — it exists to enforce uniqueness. These
    /// are *query-path* indexes: the compound keys the paged list endpoints actually sort and
    /// filter on. Without them every paged read is a collection scan followed by an in-memory
    /// sort, which is precisely the problem pagination was added to solve.
    /// </summary>
    private async Task CreateQueryPathIndexesAsync(CancellationToken ct)
    {
        var orders = context.GetCollection<Order>();
        await orders.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys
                .Ascending(o => o.BusinessId).Descending(o => o.PlacedAt)),
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys
                .Ascending(o => o.BusinessId).Ascending(o => o.CustomerUserId).Descending(o => o.PlacedAt)),
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys
                .Ascending(o => o.BusinessId).Ascending(o => o.OrderNumber))
        ], ct);

        var products = context.GetCollection<Product>();
        await products.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Product>(Builders<Product>.IndexKeys
                .Ascending(p => p.BusinessId).Ascending(p => p.Status)),
            new CreateIndexModel<Product>(Builders<Product>.IndexKeys
                .Ascending(p => p.BusinessId).Ascending(p => p.CategoryId)),
            new CreateIndexModel<Product>(Builders<Product>.IndexKeys
                .Ascending(p => p.BusinessId).Ascending(p => p.Sku))
        ], ct);

        var movements = context.GetCollection<StockMovement>();
        await movements.Indexes.CreateOneAsync(
            new CreateIndexModel<StockMovement>(Builders<StockMovement>.IndexKeys
                .Ascending(m => m.BusinessId).Ascending(m => m.ProductId).Descending(m => m.CreatedAt)),
            cancellationToken: ct);

        var ledger = context.GetCollection<LedgerEntry>();
        await ledger.Indexes.CreateOneAsync(
            new CreateIndexModel<LedgerEntry>(Builders<LedgerEntry>.IndexKeys
                .Ascending(l => l.BusinessId).Descending(l => l.OccurredAt)),
            cancellationToken: ct);

        var reviews = context.GetCollection<Review>();
        await reviews.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Review>(Builders<Review>.IndexKeys
                .Ascending(r => r.BusinessId).Ascending(r => r.ProductId).Ascending(r => r.Status)),
            // One review per customer per product, enforced by the database rather than only by
            // the service's own check — two concurrent submissions would slip past that check.
            new CreateIndexModel<Review>(
                Builders<Review>.IndexKeys.Ascending(r => r.BusinessId).Ascending(r => r.ProductId).Ascending(r => r.CustomerUserId),
                new CreateIndexOptions { Unique = true })
        ], ct);

        var wishlist = context.GetCollection<WishlistItem>();
        await wishlist.Indexes.CreateOneAsync(
            new CreateIndexModel<WishlistItem>(
                Builders<WishlistItem>.IndexKeys.Ascending(w => w.BusinessId).Ascending(w => w.CustomerUserId).Ascending(w => w.ProductId),
                new CreateIndexOptions { Unique = true }), cancellationToken: ct);

        var carts = context.GetCollection<Cart>();
        await carts.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Cart>(Builders<Cart>.IndexKeys.Ascending(c => c.BusinessId).Ascending(c => c.CustomerUserId)),
            new CreateIndexModel<Cart>(Builders<Cart>.IndexKeys.Ascending(c => c.GuestToken))
        ], ct);

        var returns = context.GetCollection<ReturnRequest>();
        await returns.Indexes.CreateOneAsync(
            new CreateIndexModel<ReturnRequest>(Builders<ReturnRequest>.IndexKeys
                .Ascending(r => r.BusinessId).Ascending(r => r.Status).Descending(r => r.CreatedAt)),
            cancellationToken: ct);

        var giftCards = context.GetCollection<GiftCard>();
        await giftCards.Indexes.CreateOneAsync(
            new CreateIndexModel<GiftCard>(Builders<GiftCard>.IndexKeys
                .Ascending(g => g.BusinessId).Ascending(g => g.CodeHash)),
            cancellationToken: ct);

        var apiKeys = context.GetCollection<ApiKey>();
        await apiKeys.Indexes.CreateOneAsync(
            new CreateIndexModel<ApiKey>(
                Builders<ApiKey>.IndexKeys.Ascending(k => k.KeyId),
                new CreateIndexOptions { Unique = true }), cancellationToken: ct);

        var content = context.GetCollection<ContentBlock>();
        await content.Indexes.CreateOneAsync(
            new CreateIndexModel<ContentBlock>(
                Builders<ContentBlock>.IndexKeys.Ascending(b => b.BusinessId).Ascending(b => b.Slug),
                new CreateIndexOptions { Unique = true, Sparse = true }), cancellationToken: ct);

        var verification = context.GetCollection<EmailVerificationToken>();
        await verification.Indexes.CreateOneAsync(
            new CreateIndexModel<EmailVerificationToken>(Builders<EmailVerificationToken>.IndexKeys.Ascending(t => t.TokenHash)),
            cancellationToken: ct);

        // TTL indexes: Mongo expires these rows itself, so nothing has to remember to sweep them.
        var idempotency = context.GetCollection<IdempotencyRecord>();
        await idempotency.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<IdempotencyRecord>(
                Builders<IdempotencyRecord>.IndexKeys.Ascending(r => r.CallerId).Ascending(r => r.Key),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<IdempotencyRecord>(
                Builders<IdempotencyRecord>.IndexKeys.Ascending(r => r.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero })
        ], ct);

        var audit = context.GetCollection<AuditLogEntry>();
        await audit.Indexes.CreateOneAsync(
            new CreateIndexModel<AuditLogEntry>(Builders<AuditLogEntry>.IndexKeys
                .Ascending(a => a.BusinessId).Descending(a => a.CreatedAt)),
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
            Status = UserStatus.Active,
            // Seeded, not self-registered — the operator who set this password owns the mailbox
            // by definition, so there is nothing for §9.34's verification flow to prove here.
            EmailVerifiedAt = DateTime.UtcNow
        };
        await users.AddAsync(admin, ct);

        logger.LogWarning(
            "Seeded PlatformSuperAdmin account. Email: {Email}{PasswordNote}",
            email,
            generatedPassword ? $" | Generated password: {password} (set PlatformAdmin:Password / PLATFORMADMIN__PASSWORD to control this)" : " | Password taken from configuration.");
    }
}
