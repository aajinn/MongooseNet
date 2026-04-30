using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongooseNet;
using MongooseNet.Tests.Fixtures;

namespace MongooseNet.Tests.Unit;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMongoose_MissingConnectionString_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddMongoose(opts =>
        {
            opts.DatabaseName = "test";
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionString*");
    }

    [Fact]
    public void AddMongoose_MissingDatabaseName_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddMongoose(opts =>
        {
            opts.ConnectionString = "mongodb://localhost:27017";
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DatabaseName*");
    }

    [Fact]
    public void AddMongoose_InvalidConnectionString_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddMongoose(opts =>
        {
            opts.ConnectionString = "not-a-mongo-uri";
            opts.DatabaseName = "test";
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*valid MongoDB connection string*");
    }

    [Fact]
    public void AddMongoose_NullConfigure_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddMongoose(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMongoose_NullServices_Throws()
    {
        IServiceCollection services = null!;
        var act = () => services.AddMongoose(opts =>
        {
            opts.ConnectionString = "mongodb://localhost:27017";
            opts.DatabaseName = "test";
        });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMongooseModel_NullServices_Throws()
    {
        IServiceCollection services = null!;
        var act = () => services.AddMongooseModel<TestDocument>();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResolveCollectionName_UsesAttribute_WhenPresent()
    {
        var name = ServiceCollectionExtensions.ResolveCollectionName(typeof(NamedDocument));
        name.Should().Be("custom_col");
    }

    [Fact]
    public void ResolveCollectionName_PluralizesClassName_WhenNoAttribute()
    {
        var name = ServiceCollectionExtensions.ResolveCollectionName(typeof(TestDocument));
        name.Should().Be("testdocuments");
    }

    [Fact]
    public void ResolveCollectionName_DoesNotDoublePluralize_WhenAlreadyEndsInS()
    {
        var name = ServiceCollectionExtensions.ResolveCollectionName(typeof(Address));
        name.Should().Be("address");
    }

    [CollectionName("custom_col")]
    private class NamedDocument : BaseDocument { }

    private class Address : BaseDocument { }
}
