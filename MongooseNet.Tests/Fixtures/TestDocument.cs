using MongoDB.Bson.Serialization.Attributes;
using MongooseNet;
using MongooseNet.Indexes;

namespace MongooseNet.Tests.Fixtures;

/// <summary>Minimal document used across all tests.</summary>
public class TestDocument : BaseDocument
{
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("email")]
    [MongoIndex(unique: true, name: "idx_test_email")]
    public string Email { get; set; } = string.Empty;

    [BsonIgnore]
    public bool PreSaveCalled { get; private set; }

    public override void PreSave()
    {
        PreSaveCalled = true;
        base.PreSave();
    }
}

/// <summary>Document whose PreSave does NOT call base — used to test timestamp enforcement.</summary>
public class BadDocument : BaseDocument
{
    public string Value { get; set; } = string.Empty;

    public override void PreSave()
    {
        // intentionally skips base.PreSave()
    }
}
