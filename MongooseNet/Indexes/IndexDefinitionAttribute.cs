namespace MongooseNet.Indexes;

/// <summary>
/// Declares a MongoDB index on a property.
/// Apply to properties of a <see cref="BaseDocument"/> subclass.
/// Indexes are created automatically when <see cref="MongooseIndexBuilder"/> is called.
/// </summary>
/// <example>
/// <code>
/// [MongoIndex(unique: true)]
/// public string Email { get; set; }
///
/// [MongoIndex(name: "email_created_idx", sparse: true)]
/// public string Email { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class MongoIndexAttribute : Attribute
{
    /// <summary>Whether the index enforces uniqueness. Default: <c>false</c>.</summary>
    public bool Unique { get; init; }

    /// <summary>Whether the index is sparse (omits documents missing the field). Default: <c>false</c>.</summary>
    public bool Sparse { get; init; }

    /// <summary>Optional custom name for the index.</summary>
    public string? Name { get; init; }

    /// <summary>Sort order: 1 for ascending (default), -1 for descending.</summary>
    public int Order { get; init; } = 1;

    public MongoIndexAttribute(bool unique = false, bool sparse = false, string? name = null, int order = 1)
    {
        Unique = unique;
        Sparse = sparse;
        Name   = name;
        Order  = order;
    }
}
