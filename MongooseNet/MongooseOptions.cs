using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace MongooseNet;

/// <summary>
/// Configuration options for MongooseNet.
/// Set properties inside the <c>AddMongoose(opts => { ... })</c> delegate,
/// or bind from configuration and register <see cref="MongooseOptionsValidator"/>
/// to validate at resolve time.
/// </summary>
public sealed class MongooseOptions
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

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

    /// <summary>
    /// Validates the options and throws <see cref="InvalidOperationException"/> if
    /// any required value is missing or out of range.
    /// Called automatically by <see cref="MongooseOptionsValidator"/> when options
    /// are resolved via <c>IOptions&lt;MongooseOptions&gt;</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("MongooseNet: ConnectionString must be set.");

        if (string.IsNullOrWhiteSpace(DatabaseName))
            throw new InvalidOperationException("MongooseNet: DatabaseName must be set.");

        if (RetryCount < 0)
            throw new InvalidOperationException("MongooseNet: RetryCount must be >= 0.");

        if (RetryDelay < TimeSpan.Zero)
            throw new InvalidOperationException("MongooseNet: RetryDelay must be >= 0.");

        if (RetryDelay > MaxRetryDelay)
            throw new InvalidOperationException($"MongooseNet: RetryDelay must be <= {MaxRetryDelay}.");

        try
        {
            _ = MongoUrl.Create(ConnectionString);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("MongooseNet: ConnectionString is not a valid MongoDB connection string.", ex);
        }
    }
}

/// <summary>
/// Validates <see cref="MongooseOptions"/> at resolve time when options are bound
/// via the <c>IOptions&lt;MongooseOptions&gt;</c> pattern.
/// </summary>
/// <remarks>
/// Register alongside your options binding:
/// <code>
/// services.AddOptions&lt;MongooseOptions&gt;()
///         .BindConfiguration("MongooseNet")
///         .ValidateOnStart();
/// services.AddSingleton&lt;IValidateOptions&lt;MongooseOptions&gt;, MongooseOptionsValidator&gt;();
/// </code>
/// </remarks>
public sealed class MongooseOptionsValidator : IValidateOptions<MongooseOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, MongooseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }
}
