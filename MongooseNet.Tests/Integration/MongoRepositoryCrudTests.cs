using MongooseNet;
using MongooseNet.Exceptions;
using MongooseNet.Tests.Fixtures;

namespace MongooseNet.Tests.Integration;

[Collection("MongoDB")]
public class MongoRepositoryCrudTests(MongoDbFixture db) : IntegrationTestBase(db)
{
    private async Task<MongoRepository<TestDocument>> RepoAsync()
    {
        SkipIfUnavailable();
        var col = await db.GetCleanCollectionAsync<TestDocument>("testdocuments");
        return new MongoRepository<TestDocument>(col);
    }

    // ── Insert ─────────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task InsertAsync_PersistsDocument()
    {
        var repo = await RepoAsync();
        var doc = new TestDocument { Name = "Alice", Email = "alice@test.com" };

        await repo.InsertAsync(doc);

        var found = await repo.GetByIdAsync(doc.Id);
        found.Should().NotBeNull();
        found!.Name.Should().Be("Alice");
    }

    [RequiresDockerFact]
    public async Task InsertAsync_FiresPreSave()
    {
        var repo = await RepoAsync();
        var doc = new TestDocument { Name = "Bob", Email = "bob@test.com" };

        await repo.InsertAsync(doc);

        doc.PreSaveCalled.Should().BeTrue();
        doc.CreatedAt.Should().NotBe(default);
        doc.UpdatedAt.Should().NotBe(default);
    }

    [RequiresDockerFact]
    public async Task InsertManyAsync_PersistsAllDocuments()
    {
        var repo = await RepoAsync();
        var docs = Enumerable.Range(1, 5)
            .Select(i => new TestDocument { Name = $"User{i}", Email = $"user{i}@test.com" })
            .ToList();

        await repo.InsertManyAsync(docs);

        (await repo.CountAsync()).Should().Be(5);
    }

    [RequiresDockerFact]
    public async Task InsertManyAsync_EmptyList_DoesNothing()
    {
        var repo = await RepoAsync();
        var act = () => repo.InsertManyAsync([]);
        await act.Should().NotThrowAsync();
        (await repo.CountAsync()).Should().Be(0);
    }

    // ── Query ──────────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var repo = await RepoAsync();
        (await repo.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [RequiresDockerFact]
    public async Task GetByIdRequiredAsync_ThrowsDocumentNotFoundException_WhenMissing()
    {
        var repo = await RepoAsync();
        var act = () => repo.GetByIdRequiredAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [RequiresDockerFact]
    public async Task FindAsync_ReturnsMatchingDocuments()
    {
        var repo = await RepoAsync();
        await repo.InsertAsync(new TestDocument { Name = "Alice", Email = "alice@test.com" });
        await repo.InsertAsync(new TestDocument { Name = "Bob",   Email = "bob@test.com" });

        var results = await repo.FindAsync(x => x.Name == "Alice");

        results.Should().HaveCount(1);
        results[0].Email.Should().Be("alice@test.com");
    }

    [RequiresDockerFact]
    public async Task FindOneAsync_ReturnsNull_WhenNoMatch()
    {
        var repo = await RepoAsync();
        (await repo.FindOneAsync(x => x.Name == "Ghost")).Should().BeNull();
    }

    [RequiresDockerFact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        var repo = await RepoAsync();
        await repo.InsertAsync(new TestDocument { Name = "A", Email = "a@test.com" });
        await repo.InsertAsync(new TestDocument { Name = "B", Email = "b@test.com" });

        (await repo.CountAsync()).Should().Be(2);
        (await repo.CountAsync(x => x.Name == "A")).Should().Be(1);
    }

    [RequiresDockerFact]
    public async Task ExistsAsync_ReturnsTrueWhenFound()
    {
        var repo = await RepoAsync();
        await repo.InsertAsync(new TestDocument { Name = "Alice", Email = "alice@test.com" });

        (await repo.ExistsAsync(x => x.Email == "alice@test.com")).Should().BeTrue();
        (await repo.ExistsAsync(x => x.Email == "ghost@test.com")).Should().BeFalse();
    }

    [RequiresDockerFact]
    public async Task GetAllAsync_ReturnsAllDocuments()
    {
        var repo = await RepoAsync();
        await repo.InsertAsync(new TestDocument { Name = "A", Email = "a@test.com" });
        await repo.InsertAsync(new TestDocument { Name = "B", Email = "b@test.com" });

        (await repo.GetAllAsync()).Should().HaveCount(2);
    }

    // ── Save (upsert) ──────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task SaveAsync_UpdatesExistingDocument()
    {
        var repo = await RepoAsync();
        var doc = new TestDocument { Name = "Alice", Email = "alice@test.com" };
        await repo.InsertAsync(doc);

        doc.Name = "Alice Updated";
        await repo.SaveAsync(doc);

        (await repo.GetByIdRequiredAsync(doc.Id)).Name.Should().Be("Alice Updated");
    }

    [RequiresDockerFact]
    public async Task SaveAsync_InsertsWhenDocumentDoesNotExist()
    {
        var repo = await RepoAsync();
        await repo.SaveAsync(new TestDocument { Name = "New", Email = "new@test.com" });
        (await repo.CountAsync()).Should().Be(1);
    }

    // ── Update (partial) ───────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task UpdateAsync_ModifiesField_AndStampsUpdatedAt()
    {
        var repo = await RepoAsync();
        var doc = new TestDocument { Name = "Alice", Email = "alice@test.com" };
        await repo.InsertAsync(doc);
        var originalUpdatedAt = doc.UpdatedAt;

        Thread.Sleep(10);
        var update = MongoDB.Driver.Builders<TestDocument>.Update.Set(x => x.Name, "Alice V2");
        var modified = await repo.UpdateAsync(doc.Id, update);

        modified.Should().BeTrue();
        var refreshed = await repo.GetByIdRequiredAsync(doc.Id);
        refreshed.Name.Should().Be("Alice V2");
        refreshed.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [RequiresDockerFact]
    public async Task UpdateAsync_ReturnsFalse_WhenDocumentNotFound()
    {
        var repo = await RepoAsync();
        var update = MongoDB.Driver.Builders<TestDocument>.Update.Set(x => x.Name, "X");
        (await repo.UpdateAsync(Guid.NewGuid(), update)).Should().BeFalse();
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task DeleteAsync_RemovesDocument()
    {
        var repo = await RepoAsync();
        var doc = new TestDocument { Name = "Alice", Email = "alice@test.com" };
        await repo.InsertAsync(doc);

        (await repo.DeleteAsync(doc.Id)).Should().BeTrue();
        (await repo.GetByIdAsync(doc.Id)).Should().BeNull();
    }

    [RequiresDockerFact]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        var repo = await RepoAsync();
        (await repo.DeleteAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [RequiresDockerFact]
    public async Task DeleteManyAsync_RemovesMatchingDocuments()
    {
        var repo = await RepoAsync();
        await repo.InsertAsync(new TestDocument { Name = "Alice", Email = "alice@test.com" });
        await repo.InsertAsync(new TestDocument { Name = "Alice", Email = "alice2@test.com" });
        await repo.InsertAsync(new TestDocument { Name = "Bob",   Email = "bob@test.com" });

        (await repo.DeleteManyAsync(x => x.Name == "Alice")).Should().Be(2);
        (await repo.CountAsync()).Should().Be(1);
    }

    // ── Security: Guid.Empty ───────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task GetByIdAsync_EmptyGuid_Throws()
    {
        var repo = await RepoAsync();
        await FluentActions.Invoking(() => repo.GetByIdAsync(Guid.Empty))
            .Should().ThrowAsync<ArgumentException>();
    }
}
