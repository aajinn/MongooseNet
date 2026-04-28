namespace MongooseNet.Exceptions;

/// <summary>Thrown when a required document lookup returns no result.</summary>
public sealed class DocumentNotFoundException(string collectionName, object id)
    : MongooseNetException($"Document with id '{id}' was not found in collection '{collectionName}'.")
{
    /// <summary>The collection that was queried.</summary>
    public string CollectionName { get; } = collectionName;

    /// <summary>The id that was looked up.</summary>
    public object DocumentId { get; } = id;
}
