using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Vastora.Domain.Common;

public abstract class BaseEntity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Soft delete (§9.35). <see cref="IMongoRepository{T}"/> excludes flagged documents from
    /// every read, so nothing in the Application layer has to remember to filter — the only way
    /// to see one again is <c>HardDeleteAsync</c>'s absence, i.e. it stays in the collection.
    /// Deleting a Product no longer orphans the StockMovement/LedgerEntry rows pointing at it.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedByUserId { get; set; }
}
