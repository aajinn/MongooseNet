using MongoDB.Bson.Serialization.Attributes;
using MongooseNet;
using MongooseNet.Indexes;

namespace MongooseNet.Example.Models;

[CollectionName("users")]   // validated against MongoDB naming rules at startup
public class User : BaseDocument
{
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("email")]
    [MongoIndex(unique: true, name: "idx_users_email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("role")]
    [MongoIndex(name: "idx_users_role")]
    public string Role { get; set; } = "user";

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Not persisted to MongoDB. Supply on create/update; PreSave() hashes and clears it.
    /// </summary>
    [BsonIgnore]
    public string? PlainTextPassword { get; set; }

    /// <summary>
    /// Mongoose-style pre('save') hook.
    /// Hashes the plain-text password if provided, then stamps timestamps via base.
    /// </summary>
    public override void PreSave()
    {
        if (!string.IsNullOrWhiteSpace(PlainTextPassword))
        {
            // SHA-256 used here for simplicity — use BCrypt/Argon2 in production
            PasswordHash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(PlainTextPassword)));
            PlainTextPassword = null;
        }

        base.PreSave(); // stamps CreatedAt / UpdatedAt
    }
}
