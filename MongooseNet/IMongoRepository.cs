using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongooseNet;

/// <summary>
/// Contract for the MongooseNet generic repository.
/// Program against this interface for easier testing and mocking.
/// </summary>
/// <typeparam name="T">A <see cref="BaseDocument"/> subclass.</typeparam>
public interface IMongoRepository<T> where T : BaseDocument
{
    // ── Queries ────────────────────────────────────────────────────────────────

    /// <summary>Returns all documents in the collection.</summary>
    Task<List<T>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns the document with the given <paramref name="id"/>, or <c>null</c>.</summary>
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns the document with the given <paramref name="id"/>.
    /// Throws <see cref="Exceptions.DocumentNotFoundException"/> when not found.
    /// </summary>
    Task<T> GetByIdRequiredAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns all documents matching <paramref name="predicate"/>.</summary>
    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>Returns the first document matching <paramref name="predicate"/>, or <c>null</c>.</summary>
    Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>Returns the number of documents matching <paramref name="predicate"/> (all if omitted).</summary>
    Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if at least one document matches <paramref name="predicate"/>.</summary>
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    // ── Writes ─────────────────────────────────────────────────────────────────

    /// <summary>Inserts a new document. Fires <see cref="BaseDocument.PreSave"/> first.</summary>
    Task<T> InsertAsync(T document, CancellationToken ct = default);

    /// <summary>Inserts multiple documents in a single batch. Fires <see cref="BaseDocument.PreSave"/> on each.</summary>
    Task InsertManyAsync(IEnumerable<T> documents, CancellationToken ct = default);

    /// <summary>
    /// Replaces an existing document (upsert). Fires <see cref="BaseDocument.PreSave"/> first.
    /// </summary>
    Task<T> SaveAsync(T document, CancellationToken ct = default);

    /// <summary>Applies a MongoDB <see cref="UpdateDefinition{T}"/> to the document with the given id.</summary>
    Task<bool> UpdateAsync(Guid id, UpdateDefinition<T> update, CancellationToken ct = default);

    // ── Deletes ────────────────────────────────────────────────────────────────

    /// <summary>Deletes the document with the given <paramref name="id"/>. Returns <c>true</c> if deleted.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Deletes all documents matching <paramref name="predicate"/>. Returns the count deleted.</summary>
    Task<long> DeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    // ── Raw access ─────────────────────────────────────────────────────────────

    /// <summary>Direct access to the underlying <see cref="IMongoCollection{T}"/> for advanced scenarios.</summary>
    IMongoCollection<T> Collection { get; }
}
