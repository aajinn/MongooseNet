namespace MongooseNet.Example.Models;

/// <summary>Lightweight projection — only id + email fetched from MongoDB.</summary>
public record UserSummary(Guid Id, string Name, string Email, string Role);
