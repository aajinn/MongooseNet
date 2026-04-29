using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongooseNet;

/// <summary>
/// Abstract base class for all MongooseNet documents.
/// Provides <see cref="Id"/>, <see cref="CreatedAt"/>, <see cref="UpdatedAt"/>,
/// soft-delete support, and <see cref="PreSave"/> lifecycle hook.
/// </summary>
public abstract class BaseDocument
{
    /// <summary>
    /// The document's unique identifier, stored as <c>_id</c> in MongoDB.
    /// Automatically assigned a new <see cref="Guid"/> on construction.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp set once on first save. Never overwritten on subsequent saves.</summary>
    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC timestamp refreshed on every save.</summary>
    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// When set, the document is considered soft-deleted and excluded from standard queries.
    /// <c>null</c> means the document is active.
    /// </summary>
    [BsonElement("deletedAt")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? DeletedAt { get; set; }

    /// <summary>Returns <c>true</c> if this document has been soft-deleted.</summary>
    [BsonIgnore]
    public bool IsDeleted => DeletedAt.HasValue;

    /// <summary>
    /// Lifecycle hook invoked by the repository before any write operation.
    /// Override to add custom logic (e.g. password hashing, field normalisation).
    /// Always call <c>base.PreSave()</c> to ensure timestamps are stamped correctly.
    /// </summary>
    public virtual void PreSave()
    {
        var now = DateTime.UtcNow;
        if (CreatedAt == default) CreatedAt = now;
        UpdatedAt = now;
    }
}
