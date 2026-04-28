using FluentAssertions;
using MongooseNet;

namespace MongooseNet.Tests.Unit;

public class CollectionNameAttributeTests
{
    [Theory]
    [InlineData("users")]
    [InlineData("app_users")]
    [InlineData("my-collection")]
    public void ValidName_IsAccepted(string name)
    {
        var attr = new CollectionNameAttribute(name);
        attr.Name.Should().Be(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankName_Throws(string name)
    {
        var act = () => new CollectionNameAttribute(name);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NullName_Throws()
    {
        var act = () => new CollectionNameAttribute(null!);
        act.Should().Throw<ArgumentException>();
    }
}
