using FluentAssertions;
using MongoDB.Driver;
using Moq;
using MongooseNet;
using MongooseNet.Exceptions;
using MongooseNet.Tests.Fixtures;

namespace MongooseNet.Tests.Unit;

/// <summary>
/// Tests that verify guard clauses, null checks, transaction behavior, and exception wrapping
/// without requiring a real MongoDB instance.
/// </summary>
public class MongoRepositoryGuardTests
{
    private readonly Mock<IMongoCollection<TestDocument>> _collectionMock;
    private readonly Mock<IMongoClient> _clientMock;
    private readonly MongoRepository<TestDocument> _repo;

    public MongoRepositoryGuardTests()
    {
        _collectionMock = new Mock<IMongoCollection<TestDocument>>();
        _collectionMock
            .Setup(c => c.CollectionNamespace)
            .Returns(new CollectionNamespace("testdb", "testdocuments"));

        _clientMock = new Mock<IMongoClient>();
        var dbMock = new Mock<IMongoDatabase>();
        dbMock.Setup(d => d.Client).Returns(_clientMock.Object);
        _collectionMock.Setup(c => c.Database).Returns(dbMock.Object);

        _repo = new MongoRepository<TestDocument>(_collectionMock.Object);
    }

    [Fact]
    public void Constructor_NullCollection_Throws()
    {
        var act = () => new MongoRepository<TestDocument>(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("collection");
    }

    [Fact]
    public async Task GetByIdAsync_EmptyGuid_Throws()
    {
        var act = () => _repo.GetByIdAsync(Guid.Empty);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public async Task GetByIdRequiredAsync_EmptyGuid_Throws()
    {
        var act = () => _repo.GetByIdRequiredAsync(Guid.Empty);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public async Task DeleteAsync_EmptyGuid_Throws()
    {
        var act = () => _repo.DeleteAsync(Guid.Empty);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public async Task UpdateAsync_EmptyGuid_Throws()
    {
        var update = Builders<TestDocument>.Update.Set(x => x.Name, "x");
        var act = () => _repo.UpdateAsync(Guid.Empty, update);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public async Task FindAsync_NullPredicate_Throws()
    {
        var act = () => _repo.FindAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FindOneAsync_NullPredicate_Throws()
    {
        var act = () => _repo.FindOneAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExistsAsync_NullPredicate_Throws()
    {
        var act = () => _repo.ExistsAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeleteManyAsync_NullPredicate_Throws()
    {
        var act = () => _repo.DeleteManyAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InsertAsync_NullDocument_Throws()
    {
        var act = () => _repo.InsertAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InsertManyAsync_NullDocuments_Throws()
    {
        var act = () => _repo.InsertManyAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SaveAsync_NullDocument_Throws()
    {
        var act = () => _repo.SaveAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateAsync_NullUpdate_Throws()
    {
        var act = () => _repo.UpdateAsync(Guid.NewGuid(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InsertAsync_MongoException_WrappedAsMongooseNetException()
    {
        _collectionMock
            .Setup(c => c.InsertOneAsync(
                It.IsAny<TestDocument>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MongoClientException("Simulated driver error"));

        var doc = new TestDocument { Name = "test", Email = "dup@test.com" };
        var act = () => _repo.InsertAsync(doc);

        await act.Should().ThrowAsync<MongooseNetException>()
            .WithMessage("*database*");
    }

    [Fact]
    public async Task WithTransactionAsync_UsesAmbientSession_ForRepositoryWrites()
    {
        var sessionMock = new Mock<IClientSessionHandle>();
        _clientMock
            .Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionMock.Object);

        _collectionMock
            .Setup(c => c.InsertOneAsync(
                sessionMock.Object,
                It.IsAny<TestDocument>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var doc = new TestDocument { Name = "tx", Email = "tx@test.com" };

        await _repo.WithTransactionAsync(_ => _repo.InsertAsync(doc));

        sessionMock.Verify(s => s.StartTransaction(It.IsAny<TransactionOptions>()), Times.Once);
        sessionMock.Verify(s => s.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _collectionMock.Verify(c => c.InsertOneAsync(
            sessionMock.Object,
            It.IsAny<TestDocument>(),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
