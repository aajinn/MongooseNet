using MongoDB.Driver;
using MongooseNet.Indexes;
using MongooseNet.Tests.Fixtures;

namespace MongooseNet.Tests.Integration;

[Collection("MongoDB")]
public class MongooseIndexBuilderTests(MongoDbFixture db) : IntegrationTestBase(db)
{
    [RequiresDockerFact]
    public async Task EnsureIndexesAsync_CreatesUniqueIndex()
    {
        SkipIfUnavailable();
        await db.Database.DropCollectionAsync("testdocuments");

        var builder = new MongooseIndexBuilder(db.Database);
        await builder.EnsureIndexesAsync([typeof(TestDocument).Assembly]);

        var collection = db.Database.GetCollection<TestDocument>("testdocuments");
        var indexes = await collection.Indexes.List().ToListAsync();

        indexes.Should().HaveCountGreaterThanOrEqualTo(2);

        var emailIndex = indexes.FirstOrDefault(i => i["name"].AsString == "idx_test_email");
        emailIndex.Should().NotBeNull("the unique email index should have been created");
        emailIndex!["unique"].AsBoolean.Should().BeTrue();
    }

    [RequiresDockerFact]
    public async Task EnsureIndexesAsync_IsIdempotent()
    {
        SkipIfUnavailable();
        await db.Database.DropCollectionAsync("testdocuments");
        var builder = new MongooseIndexBuilder(db.Database);

        await builder.EnsureIndexesAsync([typeof(TestDocument).Assembly]);
        var act = () => builder.EnsureIndexesAsync([typeof(TestDocument).Assembly]);
        await act.Should().NotThrowAsync();
    }

    [RequiresDockerFact]
    public async Task EnsureIndexesAsync_NullAssemblies_Throws()
    {
        SkipIfUnavailable();
        var builder = new MongooseIndexBuilder(db.Database);
        await FluentActions.Invoking(() => builder.EnsureIndexesAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [RequiresDockerFact]
    public async Task EnsureIndexesAsync_EmptyAssemblies_DoesNotThrow()
    {
        SkipIfUnavailable();
        var builder = new MongooseIndexBuilder(db.Database);
        await FluentActions.Invoking(() => builder.EnsureIndexesAsync([]))
            .Should().NotThrowAsync();
    }
}
