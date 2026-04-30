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
    private static readonly AsyncLocal<IClientSessionHandle?> s_currentSession = new();

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
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _collectionName = collection.CollectionNamespace.CollectionName;
        _client = collection.Database.Client;

        if (options is not null)
            options.Validate();

        _filterSoftDeleted = options?.FilterSoftDeleted ?? true;
        _retryCount = options?.RetryCount ?? 3;
        _retryDelay = options?.RetryDelay ?? TimeSpan.FromMilliseconds(200);
    }

    /// <inheritdoc/>
    public IMongoCollection<T> Collection => _collection;

    /// <summary>
    /// Gets the current ambient MongoDB session established by <see cref="WithTransactionAsync"/>.
    /// All repository operations automatically participate when this is non-null.
    /// </summary>
    protected internal static IClientSessionHandle? CurrentSession => s_currentSession.Value;

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

    /// <inheritdoc/>
    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
        => await Execute(() => Find(ActiveFilter).ToListAsync(ct), ct);

    /// <inheritdoc/>
    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        var filter = Builders<T>.Filter.And(
            Builders<T>.Filter.Eq(x => x.Id, id),
            ActiveFilter);
        return await Execute(() => Find(filter).FirstOrDefaultAsync(ct), ct);
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
        return await Execute(() => Find(CombineWithActive(predicate)).ToListAsync(ct), ct);
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
            Find(CombineWithActive(predicate))
                .Project(projection)
                .ToListAsync(ct), ct);
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
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "Page must be >= 1.");
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), "PageSize must be >= 1.");

        var filter = predicate is not null
            ? CombineWithActive(predicate)
            : ActiveFilter;

        var countTask = Execute(() => CountDocumentsAsync(filter, ct), ct);

        var cursor = Find(filter);
        if (orderBy is not null)
            cursor = descending ? cursor.SortByDescending(orderBy) : cursor.SortBy(orderBy);

        var itemsTask = Execute(() => cursor
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct), ct);

        await Task.WhenAll(countTask, itemsTask);

        return new PagedResult<T>
        {
            Items = itemsTask.Result,
            TotalCount = countTask.Result,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc/>
    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await Execute(() => Find(CombineWithActive(predicate)).FirstOrDefaultAsync(ct), ct);
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
            CurrentSession is null
                ? _collection.FindOneAndUpdateAsync(CombineWithActive(predicate), combined, options, ct)
                : _collection.FindOneAndUpdateAsync(CurrentSession, CombineWithActive(predicate), combined, options, ct), ct);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<T> StreamAsync(
        Expression<Func<T, bool>>? predicate = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var filter = predicate is not null
            ? CombineWithActive(predicate)
            : ActiveFilter;

        var cursor = await Execute(() => FindAsyncCursor(filter, ct), ct);

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
        return await Execute(() => CountDocumentsAsync(filter, ct), ct);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await Execute(() => Find(CombineWithActive(predicate)).AnyAsync(ct), ct);
    }

    /// <inheritdoc/>
    public async Task<T> InsertAsync(T document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.PreSave();
        await Execute(async () =>
        {
            await InsertOneAsync(document, ct);
            return true;
        }, ct);
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
            await InsertManyCoreAsync(list, ct);
            return true;
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<T> SaveAsync(T document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.PreSave();
        await Execute(() => ReplaceOneAsync(
            Builders<T>.Filter.Eq(x => x.Id, document.Id),
            document,
            new ReplaceOptions { IsUpsert = true },
            ct), ct);
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
            UpdateOneAsync(Builders<T>.Filter.Eq(x => x.Id, id), combined, ct), ct);

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

        return await Execute(() => BulkWriteCoreAsync(list, options, ct), ct);
    }

    /// <inheritdoc/>
    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        var update = Builders<T>.Update
            .Set(x => x.DeletedAt, DateTime.UtcNow)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var filter = Builders<T>.Filter.And(
            Builders<T>.Filter.Eq(x => x.Id, id),
            Builders<T>.Filter.Eq(x => x.DeletedAt, null));

        var result = await Execute(() => UpdateOneAsync(filter, update, ct), ct);
        return result.ModifiedCount > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        var update = Builders<T>.Update
            .Set(x => x.DeletedAt, (DateTime?)null)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var filter = Builders<T>.Filter.And(
            Builders<T>.Filter.Eq(x => x.Id, id),
            Builders<T>.Filter.Ne(x => x.DeletedAt, null));

        var result = await Execute(() => UpdateOneAsync(filter, update, ct), ct);
        return result.ModifiedCount > 0;
    }

    /// <inheritdoc/>
    public async Task<List<T>> GetDeletedAsync(CancellationToken ct = default)
    {
        var filter = Builders<T>.Filter.Ne(x => x.DeletedAt, null);
        return await Execute(() => Find(filter).ToListAsync(ct), ct);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfEmptyGuid(id);
        var result = await Execute(() => DeleteOneAsync(Builders<T>.Filter.Eq(x => x.Id, id), ct), ct);
        return result.DeletedCount > 0;
    }

    /// <inheritdoc/>
    public async Task<long> DeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var result = await Execute(() => DeleteManyAsync(Builders<T>.Filter.Where(predicate), ct), ct);
        return result.DeletedCount;
    }

    /// <inheritdoc/>
    public async Task WithTransactionAsync(
        Func<IClientSessionHandle, Task> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var existingSession = CurrentSession;
        if (existingSession is not null)
        {
            await operation(existingSession);
            return;
        }

        using var session = await _client.StartSessionAsync(cancellationToken: ct);
        s_currentSession.Value = session;

        try
        {
            session.StartTransaction();
            await operation(session);
            await session.CommitTransactionAsync(ct);
        }
        catch
        {
            if (session.IsInTransaction)
                await session.AbortTransactionAsync(ct);
            throw;
        }
        finally
        {
            s_currentSession.Value = null;
        }
    }

    /// <summary>
    /// Wraps every MongoDB driver call to translate <see cref="MongoException"/> into
    /// <see cref="MongooseNetException"/>, with optional exponential back-off retry
    /// for transient errors.
    /// </summary>
    private async Task<TResult> Execute<TResult>(Func<Task<TResult>> operation, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await operation();
            }
            catch (MongoException ex) when (IsTransient(ex) && ShouldRetry(ex) && attempt < _retryCount)
            {
                attempt++;
                var delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, ct);
            }
            catch (MongoException ex)
            {
                throw new MongooseNetException("A database error occurred. See inner exception for details.", ex);
            }
        }
    }

    private bool ShouldRetry(MongoException ex) => CurrentSession is null && !IsDuplicateKey(ex);

    private static bool IsTransient(MongoException ex) => ex is
        MongoConnectionException or
        MongoConnectionPoolPausedException or
        MongoNotPrimaryException or
        MongoNodeIsRecoveringException or
        MongoExecutionTimeoutException;

    private static bool IsDuplicateKey(MongoException ex) => ex switch
    {
        MongoWriteException writeException => writeException.WriteError?.Category == ServerErrorCategory.DuplicateKey,
        MongoBulkWriteException<T> bulkWriteException => bulkWriteException.WriteErrors.Any(e => e.Category == ServerErrorCategory.DuplicateKey),
        _ => false,
    };

    private IFindFluent<T, T> Find(FilterDefinition<T> filter) =>
        CurrentSession is null
            ? _collection.Find(filter)
            : _collection.Find(CurrentSession, filter);

    private Task<IAsyncCursor<T>> FindAsyncCursor(FilterDefinition<T> filter, CancellationToken ct) =>
        CurrentSession is null
            ? _collection.FindAsync(filter, cancellationToken: ct)
            : _collection.FindAsync(CurrentSession, filter, cancellationToken: ct);

    private Task<long> CountDocumentsAsync(FilterDefinition<T> filter, CancellationToken ct) =>
        CurrentSession is null
            ? _collection.CountDocumentsAsync(filter, cancellationToken: ct)
            : _collection.CountDocumentsAsync(CurrentSession, filter, cancellationToken: ct);

    private Task InsertOneAsync(T document, CancellationToken ct) =>
        CurrentSession is null
            ? _collection.InsertOneAsync(document, cancellationToken: ct)
            : _collection.InsertOneAsync(CurrentSession, document, cancellationToken: ct);

    private Task InsertManyCoreAsync(IEnumerable<T> documents, CancellationToken ct) =>
        CurrentSession is null
            ? _collection.InsertManyAsync(documents, cancellationToken: ct)
            : _collection.InsertManyAsync(CurrentSession, documents, cancellationToken: ct);

    private Task<ReplaceOneResult> ReplaceOneAsync(
        FilterDefinition<T> filter,
        T document,
        ReplaceOptions options,
        CancellationToken ct) =>
        CurrentSession is null
            ? _collection.ReplaceOneAsync(filter, document, options, ct)
            : _collection.ReplaceOneAsync(CurrentSession, filter, document, options, ct);

    private Task<UpdateResult> UpdateOneAsync(FilterDefinition<T> filter, UpdateDefinition<T> update, CancellationToken ct) =>
        CurrentSession is null
            ? _collection.UpdateOneAsync(filter, update, cancellationToken: ct)
            : _collection.UpdateOneAsync(CurrentSession, filter, update, cancellationToken: ct);

    private Task<BulkWriteResult<T>> BulkWriteCoreAsync(IEnumerable<WriteModel<T>> requests, BulkWriteOptions? options, CancellationToken ct) =>
        CurrentSession is null
            ? _collection.BulkWriteAsync(requests, options, ct)
            : _collection.BulkWriteAsync(CurrentSession, requests, options, ct);

    private Task<DeleteResult> DeleteOneAsync(FilterDefinition<T> filter, CancellationToken ct) =>
        CurrentSession is null
            ? _collection.DeleteOneAsync(filter, ct)
            : _collection.DeleteOneAsync(CurrentSession, filter, null, ct);

    private Task<DeleteResult> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken ct) =>
        CurrentSession is null
            ? _collection.DeleteManyAsync(filter, ct)
            : _collection.DeleteManyAsync(CurrentSession, filter, null, ct);

    private static void ThrowIfEmptyGuid(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
    }
}


