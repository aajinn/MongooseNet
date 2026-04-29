using System.Linq.Expressions;
using MongoDB.Driver;
using MongooseNet.Exceptions;

namespace MongooseNet;

/// <summary>
/// Mongoose-inspired generic repository for MongoDB.
/// Wraps <see cref="IMongoCollection{T}"/> and provides CRUD, fluent LINQ queries,
/// and automatic <see cref="BaseDocument.PreSave"/> lifecycle hooks.
/// </summary>
/// <typeparam name="T">A non-abstract <see cref="BaseDocument"/> subclass.</typeparam>
public class MongoRepository<T> : IMongoRepository<T> where T : BaseDocument
{
    private readonly IMongoCollection<T> _collection;
    private readonly string _collectionName;

    // Cached update definition for stamping UpdatedAt on partial updates
    private static readonly UpdateDefinition<T> s_touchUpdatedAt =
        Builders<T>.Update.Set(x => x.UpdatedAt, DateTime.UtcNow);

    /// <summary>
    /// Initialises the repository with an existing <see cref="IMongoCollection{T}"/>.
    /// Prefer using <see cref="ServiceCollectionExtensions.AddMongoose"/> for DI registration.
    /// </summary>
    public MongoRepository(IMongoCollection<T> collection)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _collectionName = collection.CollectionNamespace.CollectionName;
    }

    /// <inheritdoc/>
    public IMongoCollection<T> Collection => _collection;

    // ── Queries ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
        => await Execute(() => _collection.Find(_ => true).ToListAsync(ct));

    /// <inheritdoc/>
    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        return await Execute(() => _collection.Find(x => x.Id == id).FirstOrDefaultAsync(ct));
    }

    /// <inheritdoc/>
    public async Task<T> GetByIdRequiredAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await GetByIdAsync(id, ct);
        return doc ?? throw new DocumentNotFoundException(_collectionName, id);
    }

    /// <inheritdoc/>
    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await Execute(() => _collection.Find(predicate).ToListAsync(ct));
    }

    /// <inheritdoc/>
    public async Task<PagedResult<T>> PageAsync(
        Expression<Func<T, bool>>? predicate = null,
        int page = 1,
        int pageSize = 20,
        Expression<Func<T, object>>? orderBy = null,
        bool descending = false,
        CancellationToken ct = default)
    {
        if (page < 1)      throw new ArgumentOutOfRangeException(nameof(page),     "Page must be >= 1.");
        if (pageSize < 1)  throw new ArgumentOutOfRangeException(nameof(pageSize), "PageSize must be >= 1.");

        var filter = predicate ?? (_ => true);

        // Run count and data fetch in parallel
        var countTask = Execute(() => _collection.CountDocumentsAsync(filter, cancellationToken: ct));

        var cursor = _collection.Find(filter);

        if (orderBy is not null)
            cursor = descending ? cursor.SortByDescending(orderBy) : cursor.SortBy(orderBy);

        var itemsTask = Execute(() => cursor
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct));

        await Task.WhenAll(countTask, itemsTask);

        return new PagedResult<T>
        {
            Items      = itemsTask.Result,
            TotalCount = countTask.Result,
            Page       = page,
            PageSize   = pageSize,
        };
    }

    /// <inheritdoc/>
    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await Execute(() => _collection.Find(predicate).FirstOrDefaultAsync(ct));
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        => await Execute(() =>
            _collection.CountDocumentsAsync(predicate ?? (_ => true), cancellationToken: ct));

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await Execute(() => _collection.Find(predicate).AnyAsync(ct));
    }

    // ── Writes ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<T> InsertAsync(T document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.PreSave();
        await Execute(async () =>
        {
            await _collection.InsertOneAsync(document, cancellationToken: ct);
            return true;
        });
        return document;
    }

    /// <inheritdoc/>
    public async Task InsertManyAsync(IEnumerable<T> documents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var list = documents.ToList();
        if (list.Count == 0) return;
        foreach (var doc in list) doc.PreSave();
        await Execute(async () =>
        {
            await _collection.InsertManyAsync(list, cancellationToken: ct);
            return true;
        });
    }

    /// <inheritdoc/>
    public async Task<T> SaveAsync(T document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.PreSave();
        await Execute(() => _collection.ReplaceOneAsync(
            x => x.Id == document.Id,
            document,
            new ReplaceOptions { IsUpsert = true },
            ct));
        return document;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Automatically appends an <c>updatedAt</c> stamp to the provided update definition
    /// so partial updates keep the timestamp consistent.
    /// </remarks>
    public async Task<bool> UpdateAsync(Guid id, UpdateDefinition<T> update, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        ArgumentNullException.ThrowIfNull(update);

        // Always stamp updatedAt on partial updates
        var combined = Builders<T>.Update.Combine(update,
            Builders<T>.Update.Set(x => x.UpdatedAt, DateTime.UtcNow));

        var result = await Execute(() =>
            _collection.UpdateOneAsync(x => x.Id == id, combined, cancellationToken: ct));

        return result.ModifiedCount > 0;
    }

    // ── Deletes ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        var result = await Execute(() => _collection.DeleteOneAsync(x => x.Id == id, ct));
        return result.DeletedCount > 0;
    }

    /// <inheritdoc/>
    public async Task<long> DeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var result = await Execute(() => _collection.DeleteManyAsync(predicate, ct));
        return result.DeletedCount;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps every MongoDB driver call to translate <see cref="MongoException"/> into
    /// <see cref="MongooseNetException"/>, preventing driver internals from leaking
    /// through the public API surface.
    /// </summary>
    private static async Task<TResult> Execute<TResult>(Func<Task<TResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (MongoException ex)
        {
            throw new MongooseNetException(
                $"A MongoDB error occurred: {ex.Message}", ex);
        }
    }

    private static void ThrowIfEmptyGuid(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
    }
}
