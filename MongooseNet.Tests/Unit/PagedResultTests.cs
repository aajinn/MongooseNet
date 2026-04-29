namespace MongooseNet.Tests.Unit;

public class PagedResultTests
{
    // ── TotalPages ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,  10, 0)]
    [InlineData(1,  10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(20, 10, 2)]
    [InlineData(21, 10, 3)]
    [InlineData(100, 7, 15)]
    public void TotalPages_IsCalculatedCorrectly(long total, int pageSize, int expected)
    {
        var result = new PagedResult<string> { TotalCount = total, PageSize = pageSize, Page = 1 };
        result.TotalPages.Should().Be(expected);
    }

    [Fact]
    public void TotalPages_WhenPageSizeIsZero_ReturnsZero()
    {
        var result = new PagedResult<string> { TotalCount = 100, PageSize = 0, Page = 1 };
        result.TotalPages.Should().Be(0);
    }

    // ── HasNextPage / HasPreviousPage ──────────────────────────────────────────

    [Fact]
    public void HasNextPage_IsFalse_OnLastPage()
    {
        var result = new PagedResult<string> { TotalCount = 20, PageSize = 10, Page = 2 };
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void HasNextPage_IsTrue_WhenMorePagesExist()
    {
        var result = new PagedResult<string> { TotalCount = 21, PageSize = 10, Page = 2 };
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void HasPreviousPage_IsFalse_OnFirstPage()
    {
        var result = new PagedResult<string> { TotalCount = 100, PageSize = 10, Page = 1 };
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void HasPreviousPage_IsTrue_AfterFirstPage()
    {
        var result = new PagedResult<string> { TotalCount = 100, PageSize = 10, Page = 2 };
        result.HasPreviousPage.Should().BeTrue();
    }

    // ── Items default ──────────────────────────────────────────────────────────

    [Fact]
    public void Items_DefaultsToEmptyList()
    {
        var result = new PagedResult<string>();
        result.Items.Should().NotBeNull().And.BeEmpty();
    }

    // ── Single page scenario ───────────────────────────────────────────────────

    [Fact]
    public void SinglePage_HasNoPreviousOrNextPage()
    {
        var result = new PagedResult<string> { TotalCount = 5, PageSize = 10, Page = 1 };
        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeFalse();
        result.TotalPages.Should().Be(1);
    }
}
