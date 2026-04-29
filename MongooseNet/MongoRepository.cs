using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using MongoDB.Driver;
using MongooseNet.Exceptions;

namespace MongooseNet;

/// <summary>
/// Mongoose-inspired generic repository for MongoDB.
/// Wraps <see cref="IMongoCollection{T}"/> and provides CRUD, pagination, soft delete,
/// streaming, projection, bulk writes, transactions, and automatic lifecycle hooks.
/// </summary>
/// <typeparam name="T">A non-abstract <see cref="BaseDocument"/> subclass.</typeparam>
public class MongoRepository<T> : IMongoRepository<T> where T : BaseDocument
{
    private readonly IMongoCollection<T> _collection;
    private readonly IMongoClient _client;
    private readonly string _collectionName;
    private readonly bool _filterSoftDeleted;
    private readonly int _retryCount;
    private readonly TimeSpan _retryDelay;

    /// <summary>
    /// Initialises the repository.
    /// Prefer using <see cref="ServiceCollectionExtensions.AddMongoose"/> for DI registration.
    /// </summary>
    public MongoRepository(IMongoCollection<T> collection, MongooseOptions? options = null)
    {
        _collection       = collection ?? throw new ArgumentNullException(nameof(collection));
        _collectionName   = collection.CollectionNamespace.CollectionName;
        _client           = collection.Database.Client;
        _filterSoftDeleted = options?.FilterSoftDeleted ?? true;
        _retryCount       = options?.RetryCount ?? 3;
        _retryDelay       = options?.RetryDelay ?? TimeSpan.FromMilliseconds(200);
    }

    /// <inheritdoc/>
    public IMongoCollection<T> Collection => _collection;

    // ── Soft-delete filter ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a filter that excludes soft-deleted documents when
    /// <see cref="MongooseOptions.FilterSoftDeleted"/> is <c>true</c>.
    /// </summary>
    private FilterDefinition<T> ActiveFilter =>
        _filterSoftDeleted
            ? Builders<T>.Filter.Eq(x => x.DeletedAt, null)
            : Builders<T>.Filter.Empty;

    private FilterDefinition<T> CombineWithActive(Expression<Func<T, bool>> predicate) =>
        _filterSoftDeleted
            ? Builders<T>.Filter.And(Builders<T>.Filter.Where(predicate), ActiveFilter)
            : Builders<T>.Filter.Where(predicate);

    // ── Queries ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
        => await Execute(() => _collection.Find(ActiveFilter).ToListAsync(ct));

    /// <inheritdoc/>
    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        var filter = Builders<T>.Filter.And(
            Builders<T>.Filter.Eq(x => x.Id, id),
            ActiveFilter);
        return await Execute(() => _collection.Find(filter).FirstOrDefaultAsync(ct));
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
        return await Execute(() => _collection.Find(CombineWithActive(predicate)).ToListAsync(ct));
    }

    /// <inheritdoc/>
    public async Task<List<TProjection>> FindProjectedAsync<TProjection>(
        Expression<Func<T, bool>> predicate,
        ProjectionDefinition<T, TProjection> projection,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(projection);
        return await Execute(() =>
            _collection
                .Find(CombineWithActive(predicate))
                .Project(projection)
                .ToListAsync(ct));
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
        if (page < 1)     throw new ArgumentOutOfRangeException(nameof(page),     "Page must be >= 1.");
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), "PageSize must be >= 1.");

        var filter = predicate is not null
            ? CombineWithActive(predicate)
            : ActiveFilter;

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
        return await Execute(() => _collection.Find(CombineWithActive(predicate)).FirstOrDefaultAsync(ct));
    }

    /// <inheritdoc/>
    public async Task<T?> FindOneAndUpdateAsync(
        Expression<Func<T, bool>> predicate,
        UpdateDefinition<T> update,
        bool returnAfter = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(update);

        var combined = Builders<T>.Update.Combine(
            update,
            Builders<T>.Update.Set(x => x.UpdatedAt, DateTime.UtcNow));

        var options = new FindOneAndUpdateOptions<T>
        {
            ReturnDocument = returnAfter ? ReturnDocument.After : ReturnDocument.Before
        };

        return await Execute(() =>
            _collection.FindOneAndUpdateAsync(CombineWithActive(predicate), combined, options, ct));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<T> StreamAsync(
        Expression<Func<T, bool>>? predicate = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var filter = predicate is not null
            ? CombineWithActive(predicate)
            : ActiveFilter;

        IAsyncCursor<T> cursor;
        try
        {
            cursor = await _collection.FindAsync(filter, cancellationToken: ct);
        }
        catch (MongoException ex)
        {
            throw new MongooseNetException("A database error occurred. See inner exception for details.", ex);
        }

        using (cursor)
        {
            while (await cursor.MoveNextAsync(ct))
            {
                foreach (var doc in cursor.Current)
                    yield return doc;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var filter = predicate is not null
            ? CombineWithActive(predicate)
            : ActiveFilter;
        return await Execute(() => _collection.CountDocumentsAsync(filter, cancellationToken: ct));
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await Execute(() => _collection.Find(CombineWithActive(predicate)).AnyAsync(ct));
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
    public async Task<bool> UpdateAsync(Guid id, UpdateDefinition<T> update, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        ArgumentNullException.ThrowIfNull(update);

        var combined = Builders<T>.Update.Combine(
            update,
            Builders<T>.Update.Set(x => x.UpdatedAt, DateTime.UtcNow));

        var result = await Execute(() =>
            _collection.UpdateOneAsync(x => x.Id == id, combined, cancellationToken: ct));

        return result.ModifiedCount > 0;
    }

    /// <inheritdoc/>
    public async Task<BulkWriteResult<T>> BulkWriteAsync(
        IEnumerable<WriteModel<T>> requests,
        BulkWriteOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var list = requests.ToList();
        if (list.Count == 0)
            throw new ArgumentException("At least one write model is required.", nameof(requests));

        return await Execute(() => _collection.BulkWriteAsync(list, options, ct));
    }

    // ── Soft delete ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        var update = Builders<T>.Update
            .Set(x => x.DeletedAt, DateTime.UtcNow)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        // Only soft-delete active documents
        var filter = Builders<T>.Filter.And(
            Builders<T>.Filter.Eq(x => x.Id, id),
            Builders<T>.Filter.Eq(x => x.DeletedAt, null));

        var result = await Execute(() => _collection.UpdateOneAsync(filter, update, cancellationToken: ct));
        return result.ModifiedCount > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        var update = Builders<T>.Update
            .Set(x => x.DeletedAt, (DateTime?)null)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        // Only restore soft-deleted documents
        var filter = Builders<T>.Filter.And(
            Builders<T>.Filter.Eq(x => x.Id, id),
            Builders<T>.Filter.Ne(x => x.DeletedAt, null));

        var result = await Execute(() => _collection.UpdateOneAsync(filter, update, cancellationToken: ct));
        return result.ModifiedCount > 0;
    }

    /// <inheritdoc/>
    public async Task<List<T>> GetDeletedAsync(CancellationToken ct = default)
    {
        var filter = Builders<T>.Filter.Ne(x => x.DeletedAt, null);
        return await Execute(() => _collection.Find(filter).ToListAsync(ct));
    }

    // ── Hard deletes ───────────────────────────────────────────────────────────

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

    // ── Transactions ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task WithTransactionAsync(
        Func<IClientSessionHandle, Task> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var session = await _client.StartSessionAsync(cancellationToken: ct);
        session.StartTransaction();
        try
        {
            await operation(session);
            await session.CommitTransactionAsync(ct);
        }
        catch
        {
            await session.AbortTransactionAsync(ct);
            throw;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps every MongoDB driver call to translate <see cref="MongoException"/> into
    /// <see cref="MongooseNetException"/>, with optional exponential back-off retry
    /// for transient errors.
    /// </summary>
    private async Task<TResult> Execute<TResult>(Func<Task<TResult>> operation)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await operation();
            }
            catch (MongoException ex) when (IsTransient(ex) && attempt < _retryCount)
            {
                attempt++;
                var delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay);
            }
            catch (MongoException ex)
            {
                // Sanitise message — don't expose raw topology/connection details
                throw new MongooseNetException("A database error occurred. See inner exception for details.", ex);
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> for transient MongoDB errors that are safe to retry
    /// (network timeouts, not-primary, connection pool exhausted).
    /// </summary>
    private static bool IsTransient(MongoException ex) => ex is
        MongoConnectionException or
        MongoConnectionPoolPausedException or
        MongoNotPrimaryException or
        MongoNodeIsRecoveringException or
        MongoExecutionTimeoutException;

    private static void ThrowIfEmptyGuid(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
    }
}
