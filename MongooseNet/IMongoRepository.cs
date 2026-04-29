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

    /// <summary>Returns all documents in the collection (excludes soft-deleted by default).</summary>
    Task<List<T>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns the document with the given <paramref name="id"/>, or <c>null</c>.</summary>
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns the document with the given <paramref name="id"/>.
    /// Throws <see cref="Exceptions.DocumentNotFoundException"/> when not found.
    /// </summary>
    Task<T> GetByIdRequiredAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns all documents matching <paramref name="predicate"/> (excludes soft-deleted by default).</summary>
    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>
    /// Returns a projected list — only the fields in <typeparamref name="TProjection"/> are fetched
    /// from MongoDB, reducing bandwidth and memory usage.
    /// </summary>
    /// <typeparam name="TProjection">The projection type.</typeparam>
    /// <param name="predicate">Filter expression.</param>
    /// <param name="projection">Projection definition built via <c>Builders&lt;T&gt;.Projection</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<TProjection>> FindProjectedAsync<TProjection>(
        Expression<Func<T, bool>> predicate,
        ProjectionDefinition<T, TProjection> projection,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated result set, optionally filtered and sorted.
    /// Runs count and data queries in parallel for efficiency.
    /// </summary>
    Task<PagedResult<T>> PageAsync(
        Expression<Func<T, bool>>? predicate = null,
        int page = 1,
        int pageSize = 20,
        Expression<Func<T, object>>? orderBy = null,
        bool descending = false,
        CancellationToken ct = default);

    /// <summary>Returns the first document matching <paramref name="predicate"/>, or <c>null</c>.</summary>
    Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>
    /// Atomically finds a document matching <paramref name="predicate"/>, applies
    /// <paramref name="update"/>, and returns the resulting document.
    /// Returns <c>null</c> if no document matched.
    /// </summary>
    /// <param name="predicate">Filter expression.</param>
    /// <param name="update">Update definition.</param>
    /// <param name="returnAfter">
    /// When <c>true</c> (default), returns the document after the update.
    /// When <c>false</c>, returns the document before the update.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<T?> FindOneAndUpdateAsync(
        Expression<Func<T, bool>> predicate,
        UpdateDefinition<T> update,
        bool returnAfter = true,
        CancellationToken ct = default);

    /// <summary>
    /// Streams documents matching <paramref name="predicate"/> one at a time using a server-side cursor.
    /// Use this instead of <see cref="FindAsync"/> for large collections to avoid loading everything into memory.
    /// </summary>
    IAsyncEnumerable<T> StreamAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default);

    /// <summary>Returns the number of documents matching <paramref name="predicate"/> (all if omitted).</summary>
    Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if at least one document matches <paramref name="predicate"/>.</summary>
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    // ── Writes ─────────────────────────────────────────────────────────────────

    /// <summary>Inserts a new document. Fires <see cref="BaseDocument.PreSave"/> first.</summary>
    Task<T> InsertAsync(T document, CancellationToken ct = default);

    /// <summary>Inserts multiple documents in a single batch. Fires <see cref="BaseDocument.PreSave"/> on each.</summary>
    Task InsertManyAsync(IEnumerable<T> documents, CancellationToken ct = default);

    /// <summary>Replaces an existing document (upsert). Fires <see cref="BaseDocument.PreSave"/> first.</summary>
    Task<T> SaveAsync(T document, CancellationToken ct = default);

    /// <summary>
    /// Applies a partial <see cref="UpdateDefinition{T}"/> to the document with the given id.
    /// Automatically stamps <c>updatedAt</c>.
    /// </summary>
    Task<bool> UpdateAsync(Guid id, UpdateDefinition<T> update, CancellationToken ct = default);

    /// <summary>
    /// Executes a list of mixed write operations (insert / update / delete / replace)
    /// in a single round trip to MongoDB.
    /// </summary>
    Task<BulkWriteResult<T>> BulkWriteAsync(
        IEnumerable<WriteModel<T>> requests,
        BulkWriteOptions? options = null,
        CancellationToken ct = default);

    // ── Soft delete ────────────────────────────────────────────────────────────

    /// <summary>
    /// Soft-deletes the document by stamping <see cref="BaseDocument.DeletedAt"/>.
    /// The document remains in MongoDB but is excluded from standard queries.
    /// Returns <c>true</c> if the document was found and marked deleted.
    /// </summary>
    Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Restores a soft-deleted document by clearing <see cref="BaseDocument.DeletedAt"/>.
    /// Returns <c>true</c> if the document was found and restored.
    /// </summary>
    Task<bool> RestoreAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns all soft-deleted documents in the collection.</summary>
    Task<List<T>> GetDeletedAsync(CancellationToken ct = default);

    // ── Hard deletes ───────────────────────────────────────────────────────────

    /// <summary>Permanently deletes the document with the given <paramref name="id"/>. Returns <c>true</c> if deleted.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Permanently deletes all documents matching <paramref name="predicate"/>. Returns the count deleted.</summary>
    Task<long> DeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    // ── Transactions ───────────────────────────────────────────────────────────

    /// <summary>
    /// Executes <paramref name="operation"/> inside a MongoDB multi-document transaction.
    /// Commits on success, rolls back automatically on any exception.
    /// </summary>
    /// <example>
    /// <code>
    /// await repo.WithTransactionAsync(async session =>
    /// {
    ///     await repo.InsertAsync(order);
    ///     await inventory.UpdateAsync(item.Id, update);
    /// });
    /// </code>
    /// </example>
    Task WithTransactionAsync(
        Func<IClientSessionHandle, Task> operation,
        CancellationToken ct = default);

    // ── Raw access ─────────────────────────────────────────────────────────────

    /// <summary>Direct access to the underlying <see cref="IMongoCollection{T}"/> for advanced scenarios.</summary>
    IMongoCollection<T> Collection { get; }
}
