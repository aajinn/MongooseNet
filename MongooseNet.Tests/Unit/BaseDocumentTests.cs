using FluentAssertions;
using MongooseNet.Tests.Fixtures;

namespace MongooseNet.Tests.Unit;

public class BaseDocumentTests
{
    [Fact]
    public void NewDocument_HasNonEmptyId()
    {
        var doc = new TestDocument();
        doc.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void TwoDocuments_HaveDifferentIds()
    {
        var a = new TestDocument();
        var b = new TestDocument();
        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void PreSave_SetsCreatedAtOnFirstCall()
    {
        var doc = new TestDocument();
        doc.CreatedAt.Should().Be(default);

        doc.PreSave();

        doc.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void PreSave_DoesNotOverwriteCreatedAtOnSubsequentCalls()
    {
        var doc = new TestDocument();
        doc.PreSave();
        var firstCreatedAt = doc.CreatedAt;

        // Simulate a second save
        doc.PreSave();

        doc.CreatedAt.Should().Be(firstCreatedAt);
    }

    [Fact]
    public void PreSave_AlwaysRefreshesUpdatedAt()
    {
        var doc = new TestDocument();
        doc.PreSave();
        var first = doc.UpdatedAt;

        // Small delay to ensure time advances
        Thread.Sleep(10);
        doc.PreSave();

        doc.UpdatedAt.Should().BeOnOrAfter(first);
    }

    [Fact]
    public void PreSave_SetsKindToUtc()
    {
        var doc = new TestDocument();
        doc.PreSave();

        doc.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        doc.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Override_PreSaveCalled_IsTracked()
    {
        var doc = new TestDocument();
        doc.PreSaveCalled.Should().BeFalse();
        doc.PreSave();
        doc.PreSaveCalled.Should().BeTrue();
    }
}
