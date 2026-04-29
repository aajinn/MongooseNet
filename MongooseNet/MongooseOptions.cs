namespace MongooseNet;

/// <summary>
/// Configuration options for MongooseNet.
/// Set properties inside the <c>AddMongoose(opts => { ... })</c> delegate.
/// </summary>
public sealed class MongooseOptions
{
    /// <summary>
    /// The MongoDB connection string.
    /// Example: <c>mongodb://localhost:27017</c> or a full Atlas SRV URI.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>The name of the MongoDB database to use.</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// When <c>true</c>, MongooseNet scans the provided assemblies for
    /// <see cref="BaseDocument"/> subclasses and registers their repositories automatically.
    /// Default: <c>true</c>.
    /// </summary>
    public bool AutoRegisterModels { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, standard query methods (<c>GetAllAsync</c>, <c>FindAsync</c>, etc.)
    /// automatically exclude soft-deleted documents (those with a non-null <c>deletedAt</c>).
    /// Set to <c>false</c> to include soft-deleted documents in all queries.
    /// Default: <c>true</c>.
    /// </summary>
    public bool FilterSoftDeleted { get; set; } = true;

    /// <summary>
    /// Maximum number of times to retry a transient MongoDB operation before throwing.
    /// Set to <c>0</c> to disable retries. Default: <c>3</c>.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Base delay between retries. Each retry doubles the delay (exponential back-off).
    /// Default: 200 ms.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}
