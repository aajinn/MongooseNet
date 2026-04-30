namespace MongooseNet.Example.Models;

public record CreateUserRequest(string Name, string Email, string Password, string Role = "user");

public record UpdateUserRequest(string? Name, string? Role, bool? IsActive);

public record BulkActivateRequest(List<Guid> Ids);
