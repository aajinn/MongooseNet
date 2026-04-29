using MongooseNet.Tests.Fixtures;

namespace MongooseNet.Tests.Integration;

[Collection("MongoDB")]
public class MongoRepositoryPaginationTests(MongoDbFixture db) : IntegrationTestBase(db)
{
    private async Task<MongoRepository<TestDocument>> RepoAsync()
    {
        SkipIfUnavailable();
        var col = await db.GetCleanCollectionAsync<TestDocument>("pagination_tests");
        return new MongoRepository<TestDocument>(col);
    }

    /// Seeds n documents with Name = "User1".."UserN" and Email = "userN@test.com"
    private static IEnumerable<TestDocument> Seed(int count) =>
        Enumerable.Range(1, count).Select(i => new TestDocument
        {
            Name  = $"User{i:D3}",   // zero-padded so lexicographic == numeric order
            Email = $"user{i}@test.com"
        });

    // ── Basic pagination ───────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task PageAsync_NoFilter_ReturnsFirstPage()
    {
        var repo = await RepoAsync();
        await repo.InsertManyAsync(Seed(25));

        var result = await repo.PageAsync(page: 1, pageSize: 10);

        result.Items.Should().HaveCount(10);
        result.TotalCount.Should().Be(25);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeTrue();
    }

    [RequiresDockerFact]
    public async Task PageAsync_LastPage_HasFewerItems()
    {
        var repo = await RepoAsync();
        await repo.InsertManyAsync(Seed(25));

        var result = await repo.PageAsync(page: 3, pageSize: 10);

        result.Items.Should().HaveCount(5);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
    }

    [RequiresDockerFact]
    public async Task PageAsync_PageBeyondData_ReturnsEmptyItems()
    {
        var repo = await RepoAsync();
        await repo.InsertManyAsync(Seed(5));

        var result = await repo.PageAsync(page: 99, pageSize: 10);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(5);
    }

    [RequiresDockerFact]
    public async Task PageAsync_EmptyCollection_ReturnsEmptyResult()
    {
        var repo = await RepoAsync();

        var result = await repo.PageAsync();

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    // ── Filtering ──────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task PageAsync_WithPredicate_FiltersBeforePaging()
    {
        var repo = await RepoAsync();
        await repo.InsertManyAsync(Seed(10));
        // Insert 5 extra with a different name pattern
        await repo.InsertManyAsync(Enumerable.Range(1, 5).Select(i => new TestDocument
        {
            Name  = $"Admin{i}",
            Email = $"admin{i}@test.com"
        }));

        var result = await repo.PageAsync(
            predicate: x => x.Name.StartsWith("Admin"),
            page: 1,
            pageSize: 10);

        result.TotalCount.Should().Be(5);
        result.Items.Should().HaveCount(5);
        result.Items.Should().AllSatisfy(d => d.Name.Should().StartWith("Admin"));
    }

    [RequiresDockerFact]
    public async Task PageAsync_WithPredicate_TotalCountReflectsFilterNotTotal()
    {
        var repo = await RepoAsync();
        await repo.InsertManyAsync(Seed(20));

        var result = await repo.PageAsync(
            predicate: x => x.Name.StartsWith("User01"),  // matches User010..User019 + User01 = 11
            page: 1,
            pageSize: 5);

        // TotalCount should be the filtered count, not 20
        result.TotalCount.Should().BeLessThan(20);
        result.Items.Count.Should().BeLessOrEqualTo(5);
    }

    // ── Sorting ────────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task PageAsync_SortAscending_ItemsAreOrdered()
    {
        var repo = await RepoAsync();
        await repo.InsertManyAsync(Seed(5));

        var result = await repo.PageAsync(
            page: 1,
            pageSize: 5,
            orderBy: x => x.Name,
            descending: false);

        var names = result.Items.Select(x => x.Name).ToList();
        names.Should().BeInAscendingOrder();
    }

    [RequiresDockerFact]
    public async Task PageAsync_SortDescending_ItemsAreOrdered()
    {
        var repo = await RepoAsync();
        await repo.InsertManyAsync(Seed(5));

        var result = await repo.PageAsync(
            page: 1,
            pageSize: 5,
            orderBy: x => x.Name,
            descending: true);

        var names = result.Items.Select(x => x.Name).ToList();
        names.Should().BeInDescendingOrder();
    }

    // ── Page size edge cases ───────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task PageAsync_PageSizeOne_ReturnsSingleItem()
    {
        var repo = await RepoAsync();
        await repo.InsertManyAsync(Seed(3));

        var result = await repo.PageAsync(page: 2, pageSize: 1);

        result.Items.Should().HaveCount(1);
        result.TotalPages.Should().Be(3);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
    }

    [RequiresDockerFact]
    public async Task PageAsync_PageSizeLargerThanTotal_ReturnsSinglePage()
    {
        var repo = await RepoAsync();
        await repo.InsertManyAsync(Seed(3));

        var result = await repo.PageAsync(page: 1, pageSize: 100);

        result.Items.Should().HaveCount(3);
        result.TotalPages.Should().Be(1);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    // ── Metadata consistency ───────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task PageAsync_AllPages_CoverAllDocuments()
    {
        var repo = await RepoAsync();
        await repo.InsertManyAsync(Seed(15));

        var allIds = new List<Guid>();
        int page = 1;
        PagedResult<TestDocument> result;

        do
        {
            result = await repo.PageAsync(
                page: page++,
                pageSize: 4,
                orderBy: x => x.Name);
            allIds.AddRange(result.Items.Select(x => x.Id));
        }
        while (result.HasNextPage);

        allIds.Should().HaveCount(15);
        allIds.Should().OnlyHaveUniqueItems();
    }
}
