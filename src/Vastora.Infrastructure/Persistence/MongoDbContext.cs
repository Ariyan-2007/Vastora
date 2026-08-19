using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using Vastora.Domain.Common;

namespace Vastora.Infrastructure.Persistence;

public class MongoDbContext
{
    /// <summary>
    /// Registered once, before any BsonClassMap gets built for any entity (a static constructor
    /// runs before this class's first use, and every repository goes through it). Without this,
    /// renaming or removing a field on any entity throws FormatException on every existing
    /// document that still has the old field, the moment it's next read — a schema-evolution trap
    /// that isn't hypothetical: it broke <c>Business.CustomDomain</c> → <c>ShopDomain</c> the same
    /// day that rename shipped, on real data. MongoDB is schemaless by design; stale fields on old
    /// documents are supposed to be survivable, not a 500.
    /// </summary>
    static MongoDbContext()
    {
        ConventionRegistry.Register(
            "IgnoreExtraElements",
            new ConventionPack { new IgnoreExtraElementsConvention(true) },
            _ => true);
    }

    public IMongoDatabase Database { get; }

    public MongoDbContext(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        Database = client.GetDatabase(settings.Value.DatabaseName);
    }

    public IMongoCollection<T> GetCollection<T>() where T : BaseEntity =>
        Database.GetCollection<T>(CollectionNames.For<T>());
}
