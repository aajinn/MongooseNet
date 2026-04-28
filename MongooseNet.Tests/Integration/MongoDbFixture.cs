using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace MongooseNet.Tests.Integration;

/// <summary>
/// Spins up a real MongoDB container once per test collection.
/// Tests are skipped automatically via <see cref="RequiresDockerFact"/> when
/// Docker is unavailable or the image cannot be pulled.
/// </summary>
public sealed class MongoDbFixture : IAsyncLifetime
{
    public IMongoClient Client { get; private set; } = null!;
    public IMongoDatabase Database { get; private set; } = null!;

    /// <summary>Non-null when the container could not start — causes tests to skip.</summary>
    public string? SkipReason { get; private set; }

    private MongoDbContainer? _container;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MongoDbBuilder()
                .WithImage("mongo:latest")
                .Build();

            await _container.StartAsync();

            Client = new MongoClient(_container.GetConnectionString());
            Database = Client.GetDatabase("mongoose_tests");
        }
        catch (Exception ex)
        {
            SkipReason = $"MongoDB container could not start: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>Returns a fresh collection, dropping any existing data.</summary>
    public async Task<IMongoCollection<T>> GetCleanCollectionAsync<T>(string name)
    {
        await Database.DropCollectionAsync(name);
        return Database.GetCollection<T>(name);
    }
}

[CollectionDefinition("MongoDB")]
public class MongoDbCollection : ICollectionFixture<MongoDbFixture> { }
