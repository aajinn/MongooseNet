using FluentAssertions;
using Microsoft.Extensions.Options;

namespace MongooseNet.Tests.Unit;

public class MongooseOptionsTests
{
    [Fact]
    public void Validate_InvalidConnectionString_Throws()
    {
        var options = new MongooseNet.MongooseOptions
        {
            ConnectionString = "bad-uri",
            DatabaseName = "test"
        };

        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*valid MongoDB connection string*");
    }

    [Fact]
    public void Validate_TooLargeRetryDelay_Throws()
    {
        var options = new MongooseNet.MongooseOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = "test",
            RetryDelay = TimeSpan.FromMinutes(2)
        };

        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RetryDelay must be <=*");
    }

    [Fact]
    public void Validator_InvalidOptions_ReturnsFailure()
    {
        var validator = new MongooseNet.MongooseOptionsValidator();
        var options = new MongooseNet.MongooseOptions
        {
            ConnectionString = "bad-uri",
            DatabaseName = "test"
        };

        var result = validator.Validate(Options.DefaultName, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle(x => x.Contains("valid MongoDB connection string"));
    }
}
