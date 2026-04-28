using FluentAssertions;
using MongooseNet.Indexes;

namespace MongooseNet.Tests.Unit;

public class MongoIndexAttributeTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var attr = new MongoIndexAttribute();
        attr.Unique.Should().BeFalse();
        attr.Sparse.Should().BeFalse();
        attr.Name.Should().BeNull();
        attr.Order.Should().Be(1);
    }

    [Fact]
    public void UniqueFlag_IsSet()
    {
        var attr = new MongoIndexAttribute(unique: true);
        attr.Unique.Should().BeTrue();
    }

    [Fact]
    public void SparseFlag_IsSet()
    {
        var attr = new MongoIndexAttribute(sparse: true);
        attr.Sparse.Should().BeTrue();
    }

    [Fact]
    public void CustomName_IsSet()
    {
        var attr = new MongoIndexAttribute(name: "my_idx");
        attr.Name.Should().Be("my_idx");
    }

    [Fact]
    public void DescendingOrder_IsNegative()
    {
        var attr = new MongoIndexAttribute(order: -1);
        attr.Order.Should().Be(-1);
    }
}
