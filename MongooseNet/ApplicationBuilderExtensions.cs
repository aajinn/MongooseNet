using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MongooseNet.Indexes;

// Works with both IApplicationBuilder (ASP.NET Core) and IHost (generic host)
// by depending only on IServiceProvider.
namespace MongooseNet;

/// <summary>
/// Extension methods for ensuring MongoDB indexes at application startup.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Ensures all indexes declared via <see cref="MongoIndexAttribute"/> are created in MongoDB.
    /// Call this once during application startup, after <c>builder.Build()</c>.
    /// </summary>
    /// <param name="serviceProvider">The application's root <see cref="IServiceProvider"/>.</param>
    /// <param name="assemblies">
    /// Assemblies to scan. Defaults to the calling assembly when none are provided.
    /// </param>
    /// <example>
    /// <code>
    /// var app = builder.Build();
    /// await app.Services.EnsureMongoIndexesAsync();
    /// app.Run();
    /// </code>
    /// </example>
    public static async Task EnsureMongoIndexesAsync(
        this IServiceProvider serviceProvider,
        params Assembly[] assemblies)
    {
        using var scope = serviceProvider.CreateScope();
        var indexBuilder = scope.ServiceProvider.GetRequiredService<MongooseIndexBuilder>();

        var targets = assemblies.Length > 0
            ? assemblies
            : [Assembly.GetCallingAssembly()];

        await indexBuilder.EnsureIndexesAsync(targets);
    }
}
