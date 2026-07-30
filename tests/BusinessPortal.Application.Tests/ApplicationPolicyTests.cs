using BusinessPortal.Application;

namespace BusinessPortal.Application.Tests;

public sealed class ApplicationPolicyTests
{
    [Theory]
    [InlineData(PortalRoles.Administrator, true)]
    [InlineData(PortalRoles.Manager, true)]
    [InlineData(PortalRoles.Employee, false)]
    public void Management_permission_is_role_based(string role, bool expected)
    {
        var user = new CurrentUserInfo("user", Guid.NewGuid(), "Org", "User", new HashSet<string> { role });
        Assert.Equal(expected, user.CanManage);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    public void Page_number_is_bounded(int requested, int expected) =>
        Assert.Equal(expected, new PageRequest(Page: requested).SafePage);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 25)]
    [InlineData(1000, 100)]
    public void Page_size_is_bounded(int requested, int expected) =>
        Assert.Equal(expected, new PageRequest(PageSize: requested).SafePageSize);

    [Fact]
    public void Page_result_calculates_total_pages() =>
        Assert.Equal(3, new PageResult<int>([1], 41, 1, 20).TotalPages);
}
