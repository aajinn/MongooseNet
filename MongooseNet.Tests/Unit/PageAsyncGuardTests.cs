using MongoDB.Driver;
using Moq;
using MongooseNet.Tests.Fixtures;

namespace MongooseNet.Tests.Unit;

/// <summary>
/// Guard-clause tests for PageAsync that don't need a real MongoDB instance.
/// </summary>
public class PageAsyncGuardTests
{
    private readonly MongoRepository<TestDocument> _repo;

    public PageAsyncGuardTests()
    {
        var mock = new Mock<IMongoCollection<TestDocument>>();
        mock.Setup(c => c.CollectionNamespace)
            .Returns(new CollectionNamespace("testdb", "testdocuments"));

        _repo = new MongoRepository<TestDocument>(mock.Object);
    }

    [Fact]
    public async Task PageAsync_PageZero_Throws()
    {
        var act = () => _repo.PageAsync(page: 0);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("page");
    }

    [Fact]
    public async Task PageAsync_NegativePage_Throws()
    {
        var act = () => _repo.PageAsync(page: -1);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("page");
    }

    [Fact]
    public async Task PageAsync_PageSizeZero_Throws()
    {
        var act = () => _repo.PageAsync(pageSize: 0);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("pageSize");
    }

    [Fact]
    public async Task PageAsync_NegativePageSize_Throws()
    {
        var act = () => _repo.PageAsync(pageSize: -5);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("pageSize");
    }
}
