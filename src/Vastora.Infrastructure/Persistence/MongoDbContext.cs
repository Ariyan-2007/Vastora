using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vastora.Domain.Common;

namespace Vastora.Infrastructure.Persistence;

public class MongoDbContext
{
    public IMongoDatabase Database { get; }

    public MongoDbContext(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        Database = client.GetDatabase(settings.Value.DatabaseName);
    }

    public IMongoCollection<T> GetCollection<T>() where T : BaseEntity =>
        Database.GetCollection<T>(CollectionNames.For<T>());
}
