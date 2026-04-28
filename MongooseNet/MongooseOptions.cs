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
}
