using System.Reflection;
using MongoDB.Driver;
using MongooseNet.Exceptions;

namespace MongooseNet.Indexes;

/// <summary>
/// Scans <see cref="BaseDocument"/> subclasses for <see cref="MongoIndexAttribute"/> declarations
/// and creates the corresponding MongoDB indexes.
/// Call <see cref="EnsureIndexesAsync"/> once at application startup.
/// </summary>
public sealed class MongooseIndexBuilder(IMongoDatabase database)
{
    // Cached reflection handles — resolved once, reused across all model types.
    private static readonly MethodInfo s_getCollectionMethod =
        typeof(IMongoDatabase).GetMethod(nameof(IMongoDatabase.GetCollection))
        ?? throw new InvalidOperationException("Could not reflect IMongoDatabase.GetCollection.");

    /// <summary>
    /// Creates all indexes declared via <see cref="MongoIndexAttribute"/> across all
    /// <see cref="BaseDocument"/> subclasses found in <paramref name="assemblies"/>.
    /// Safe to call on every startup — MongoDB is idempotent for existing indexes.
    /// </summary>
    public async Task EnsureIndexesAsync(
        IEnumerable<Assembly> assemblies,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var baseType = typeof(BaseDocument);
        var errors = new List<Exception>();

        var modelTypes = assemblies
            .SelectMany(SafeGetTypes)
            .Where(t => t is { IsAbstract: false, IsClass: true } && baseType.IsAssignableFrom(t))
            .Distinct();

        foreach (var modelType in modelTypes)
        {
            try
            {
                await EnsureIndexesForTypeAsync(modelType, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Collect all failures rather than aborting on the first one
                errors.Add(new MongooseNetException(
                    $"Failed to create indexes for '{modelType.FullName}'.", ex));
            }
        }

        if (errors.Count == 1) throw errors[0];
        if (errors.Count > 1) throw new AggregateException("One or more index creation operations failed.", errors);
    }

    private async Task EnsureIndexesForTypeAsync(Type modelType, CancellationToken ct)
    {
        var collectionName = ServiceCollectionExtensions.ResolveCollectionName(modelType);

        var typedGetCollection = s_getCollectionMethod.MakeGenericMethod(modelType);
        var collection = typedGetCollection.Invoke(database, [collectionName, null])
            ?? throw new MongooseNetException($"IMongoDatabase.GetCollection returned null for '{collectionName}'.");

        var indexesProperty = collection.GetType().GetProperty("Indexes")
            ?? throw new MongooseNetException($"Could not find 'Indexes' property on collection type '{collection.GetType().Name}'.");

        var indexManager = indexesProperty.GetValue(collection)
            ?? throw new MongooseNetException($"'Indexes' property returned null for collection '{collectionName}'.");

        var indexModelType = typeof(CreateIndexModel<>).MakeGenericType(modelType);
        var indexModels = BuildIndexModels(modelType, indexModelType);

        if (indexModels.Count == 0) return;

        // Find CreateManyAsync(IEnumerable<CreateIndexModel<T>>, CancellationToken)
        var createManyMethod = indexManager.GetType()
            .GetMethods()
            .FirstOrDefault(m =>
                m.Name == "CreateManyAsync"
                && m.GetParameters() is { Length: 2 } p
                && p[0].ParameterType.IsGenericType
                && p[1].ParameterType == typeof(CancellationToken))
            ?? throw new MongooseNetException($"Could not find CreateManyAsync on index manager for '{collectionName}'.");

        var typedList = CastToTypedList(indexModels, indexModelType);

        var task = createManyMethod.Invoke(indexManager, [typedList, ct]) as Task
            ?? throw new MongooseNetException("CreateManyAsync did not return a Task.");

        await task;
    }

    private static List<object> BuildIndexModels(Type modelType, Type indexModelType)
    {
        var models = new List<object>();
        var keysBuilderType = typeof(IndexKeysDefinitionBuilder<>).MakeGenericType(modelType);
        var keysBuilder = Activator.CreateInstance(keysBuilderType)
            ?? throw new MongooseNetException($"Could not create IndexKeysDefinitionBuilder for '{modelType.Name}'.");

        // In MongoDB driver v3, Ascending/Descending accept FieldDefinition<T> not a raw string.
        // StringFieldDefinition<T> is the string-based implementation of FieldDefinition<T>.
        var fieldDefType = typeof(StringFieldDefinition<>).MakeGenericType(modelType);

        foreach (var prop in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var attr in prop.GetCustomAttributes<MongoIndexAttribute>())
            {
                var keyMethod = attr.Order >= 0 ? "Ascending" : "Descending";

                // Build a StringFieldDefinition<T>(propertyName) to pass as the field argument
                var fieldDef = Activator.CreateInstance(fieldDefType, prop.Name)
                    ?? throw new MongooseNetException(
                        $"Could not create StringFieldDefinition for property '{prop.Name}' on '{modelType.Name}'.");

                // Find Ascending/Descending overload that accepts FieldDefinition<T>
                var fieldDefBaseType = typeof(FieldDefinition<>).MakeGenericType(modelType);
                var keysDefMethod = keysBuilderType
                    .GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == keyMethod
                        && m.GetParameters() is { Length: 1 } p
                        && p[0].ParameterType.IsAssignableFrom(fieldDefType))
                    ?? throw new MongooseNetException(
                        $"Could not find '{keyMethod}(FieldDefinition<{modelType.Name}>) method on IndexKeysDefinitionBuilder<{modelType.Name}>.");

                var keysDef = keysDefMethod.Invoke(keysBuilder, [fieldDef])
                    ?? throw new MongooseNetException(
                        $"'{keyMethod}' returned null for property '{prop.Name}' on '{modelType.Name}'.");

                var options = new CreateIndexOptions
                {
                    Unique = attr.Unique,
                    Sparse = attr.Sparse,
                    Name   = attr.Name
                };

                var model = Activator.CreateInstance(indexModelType, keysDef, options)
                    ?? throw new MongooseNetException(
                        $"Could not create CreateIndexModel<{modelType.Name}> for property '{prop.Name}'.");

                models.Add(model);
            }
        }

        return models;
    }

    private static object CastToTypedList(List<object> models, Type indexModelType)
    {
        var listType = typeof(List<>).MakeGenericType(indexModelType);
        var list = Activator.CreateInstance(listType)
            ?? throw new MongooseNetException($"Could not create List<{indexModelType.Name}>.");

        var addMethod = listType.GetMethod("Add")
            ?? throw new MongooseNetException($"Could not find Add method on List<{indexModelType.Name}>.");

        foreach (var m in models) addMethod.Invoke(list, [m]);
        return list;
    }

    /// <summary>Safely enumerates types from an assembly, skipping any that fail to load.</summary>
    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
