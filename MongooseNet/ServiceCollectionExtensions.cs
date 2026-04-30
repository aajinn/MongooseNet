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
    public static IServiceCollection AddMongoose(
        this IServiceCollection services,
        Action<MongooseOptions> configure,
        params Assembly[] modelAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MongooseOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton<IMongoClient>(_ => new MongoClient(options.ConnectionString));
        services.TryAddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(options.DatabaseName));
        services.TryAddSingleton(options);
        services.TryAddScoped<MongooseIndexBuilder>();

        if (!options.AutoRegisterModels) return services;

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
        ArgumentNullException.ThrowIfNull(services);
        RegisterRepository(services, typeof(T));
        return services;
    }

    private static void RegisterRepositories(IServiceCollection services, Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var baseType = typeof(BaseDocument);

        var modelTypes = assemblies
            .SelectMany(SafeGetTypes)
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

        services.TryAddScoped(repoInterfaceType, sp => sp.GetRequiredService(repoConcreteType));
    }

    internal static string ResolveCollectionName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var attr = type.GetCustomAttribute<CollectionNameAttribute>();
        if (attr is not null) return attr.Name;

        var name = type.Name.ToLowerInvariant();
        return name.EndsWith('s') ? name : name + "s";
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
