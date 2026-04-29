using System.Text;
using System.Text.RegularExpressions;

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
    // MongoDB collection name rules:
    //   - Must not be null or whitespace
    //   - Must not contain the '$' character
    //   - Must not contain the null character (\0)
    //   - Must not start with "system."
    //   - UTF-8 encoded length must not exceed 120 bytes
    private static readonly Regex InvalidChars = new(@"[\$\x00]", RegexOptions.Compiled);

    /// <summary>The MongoDB collection name to use.</summary>
    public string Name { get; } = Validate(name);

    private static string Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(name));

        if (InvalidChars.IsMatch(name))
            throw new ArgumentException(
                "Collection name must not contain '$' or null characters.", nameof(name));

        if (name.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "Collection name must not start with 'system.'.", nameof(name));

        if (Encoding.UTF8.GetByteCount(name) > 120)
            throw new ArgumentException(
                "Collection name must not exceed 120 bytes when UTF-8 encoded.", nameof(name));

        return name;
    }
}
