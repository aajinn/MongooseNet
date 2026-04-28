using MongoDB.Bson.Serialization.Attributes;
using MongooseNet;
using MongooseNet.Indexes;

namespace MongooseNet.Example.Models;

[CollectionName("users")]
public class User : BaseDocument
{
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("email")]
    [MongoIndex(unique: true, name: "idx_users_email")]   // ← declarative unique index
    public string Email { get; set; } = string.Empty;

    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonIgnore]
    public string? PlainTextPassword { get; set; }

    /// <summary>
    /// PreSave hook — mirrors Mongoose's userSchema.pre('save').
    /// Hashes the plain-text password if provided, then stamps timestamps via base.
    /// </summary>
    public override void PreSave()
    {
        if (!string.IsNullOrWhiteSpace(PlainTextPassword))
        {
            // Replace with your preferred hashing library (BCrypt, Argon2, etc.)
            PasswordHash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(PlainTextPassword)));
            PlainTextPassword = null;
        }

        base.PreSave(); // stamps CreatedAt / UpdatedAt
    }
}
