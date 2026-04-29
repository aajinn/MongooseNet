namespace MongooseNet;

/// <summary>
/// Represents a single page of results from a paginated query.
/// </summary>
/// <typeparam name="T">The document type.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>The documents on this page.</summary>
    public List<T> Items { get; init; } = [];

    /// <summary>Total number of documents matching the query (across all pages).</summary>
    public long TotalCount { get; init; }

    /// <summary>The current page number (1-based).</summary>
    public int Page { get; init; }

    /// <summary>The number of items per page.</summary>
    public int PageSize { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>Whether there is a page after this one.</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>Whether there is a page before this one.</summary>
    public bool HasPreviousPage => Page > 1;
}
