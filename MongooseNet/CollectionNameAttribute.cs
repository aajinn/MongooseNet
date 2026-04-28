namespace MongooseNet;

/// <summary>
/// Overrides the MongoDB collection name for a <see cref="BaseDocument"/> subclass.
/// When omitted, MongooseNet defaults to the lowercase-pluralised class name
/// (e.g. <c>User</c> → <c>users</c>), matching Mongoose.js behaviour.
/// </summary>
/// <example>
/// <code>
/// [CollectionName("app_users")]
/// public class User : BaseDocument { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CollectionNameAttribute(string name) : Attribute
{
    /// <summary>The MongoDB collection name to use.</summary>
    public string Name { get; } = !string.IsNullOrWhiteSpace(name)
        ? name
        : throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(name));
}
