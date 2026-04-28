namespace MongooseNet.Tests.Integration;

/// <summary>
/// A fact that skips at discovery time if Docker is not reachable,
/// and supports runtime skipping via <see cref="IntegrationTestBase.SkipIfUnavailable"/>
/// using the xunit.skippablefact package.
/// </summary>
public sealed class RequiresDockerFact : SkippableFactAttribute
{
    private static readonly string? s_skipReason = DetectSkipReason();

    public RequiresDockerFact()
    {
        if (s_skipReason is not null)
            Skip = s_skipReason;
    }

    private static string? DetectSkipReason()
    {
        try
        {
            new Testcontainers.MongoDb.MongoDbBuilder()
                .WithImage("mongo:latest")
                .Build();
            return null;
        }
        catch (ArgumentException ex) when (ex.ParamName == "DockerEndpointAuthConfig")
        {
            return "Docker is not running or not configured.";
        }
        catch
        {
            return null; // Docker is up; image issues are handled at runtime
        }
    }
}

/// <summary>
/// Base class for integration tests. Call <see cref="SkipIfUnavailable"/> at the
/// top of each test to turn container startup failures into skips.
/// </summary>
public abstract class IntegrationTestBase(MongoDbFixture db)
{
    /// <summary>
    /// Skips the current test if the MongoDB container failed to start.
    /// Uses <c>Skip.If</c> from xunit.skippablefact.
    /// </summary>
    protected void SkipIfUnavailable()
        => Skip.If(db.SkipReason is not null, db.SkipReason ?? string.Empty);
}
