using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using MongooseNet.Indexes;

namespace MongooseNet;

/// <summary>
/// Extension methods for registering MongooseNet with the .NET DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MongooseNet with the DI container.
    /// Automatically discovers all <see cref="BaseDocument"/> subclasses in
    /// <paramref name="modelAssemblies"/> (defaults to the calling assembly) and
    /// registers a scoped <see cref="MongoRepository{T}"/> and <see cref="IMongoRepository{T}"/>
    /// for each one.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Delegate to configure <see cref="MongooseOptions"/>.</param>
    /// <param name="modelAssemblies">
    /// Assemblies to scan for <see cref="BaseDocument"/> subclasses.
    /// When empty, the calling assembly is used.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddMongoose(opts =>
    /// {
    ///     opts.ConnectionString = "mongodb://localhost:27017";
    ///     opts.DatabaseName     = "myapp";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddMongoose(
        this IServiceCollection services,
        Action<MongooseOptions> configure,
        params Assembly[] modelAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Configure and validate options
        var options = new MongooseOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("MongooseNet: ConnectionString must be set.");
        if (string.IsNullOrWhiteSpace(options.DatabaseName))
            throw new InvalidOperationException("MongooseNet: DatabaseName must be set.");

        // Singleton: IMongoClient
        services.TryAddSingleton<IMongoClient>(_ => new MongoClient(options.ConnectionString));

        // Singleton: IMongoDatabase
        services.TryAddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(options.DatabaseName));

        // Singleton: MongooseOptions (so repositories can read retry/soft-delete config)
        services.TryAddSingleton(options);

        // Scoped: index builder (used at startup via EnsureMongoIndexesAsync)
        services.TryAddScoped<MongooseIndexBuilder>();

        if (!options.AutoRegisterModels) return services;

        // Resolve assemblies to scan
        var assemblies = modelAssemblies.Length > 0
            ? modelAssemblies
            : [Assembly.GetCallingAssembly()];

        RegisterRepositories(services, assemblies);

        return services;
    }

    /// <summary>
    /// Manually registers a <see cref="MongoRepository{T}"/> and <see cref="IMongoRepository{T}"/>
    /// for a specific model type. Useful when <c>AutoRegisterModels</c> is <c>false</c>.
    /// </summary>
    public static IServiceCollection AddMongooseModel<T>(this IServiceCollection services)
        where T : BaseDocument
    {
        RegisterRepository(services, typeof(T));
        return services;
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private static void RegisterRepositories(IServiceCollection services, Assembly[] assemblies)
    {
        var baseType = typeof(BaseDocument);

        var modelTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsClass: true } && baseType.IsAssignableFrom(t))
            .Distinct();

        foreach (var modelType in modelTypes)
            RegisterRepository(services, modelType);
    }

    private static void RegisterRepository(IServiceCollection services, Type modelType)
    {
        var repoConcreteType = typeof(MongoRepository<>).MakeGenericType(modelType);
        var repoInterfaceType = typeof(IMongoRepository<>).MakeGenericType(modelType);

        services.TryAddScoped(repoConcreteType, sp =>
        {
            var db = sp.GetRequiredService<IMongoDatabase>();
            var opts = sp.GetService<MongooseOptions>();
            var collectionName = ResolveCollectionName(modelType);

            var getCollection = typeof(IMongoDatabase)
                .GetMethod(nameof(IMongoDatabase.GetCollection))!
                .MakeGenericMethod(modelType);

            var collection = getCollection.Invoke(db, [collectionName, null])!;
            return Activator.CreateInstance(repoConcreteType, collection, opts)!;
        });

        // Also register as the interface so consumers can inject IMongoRepository<T>
        services.TryAddScoped(repoInterfaceType, sp => sp.GetRequiredService(repoConcreteType));
    }

    internal static string ResolveCollectionName(Type type)
    {
        var attr = type.GetCustomAttribute<CollectionNameAttribute>();
        if (attr is not null) return attr.Name;

        // Mongoose default: lowercase plural of class name
        var name = type.Name.ToLowerInvariant();
        return name.EndsWith('s') ? name : name + "s";
    }
}
