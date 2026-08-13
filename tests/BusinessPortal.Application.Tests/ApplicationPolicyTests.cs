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

    [Theory]
    [InlineData(1, "CLI-0001", "PRJ-0001", "WI-0001", "TE-0001")]
    [InlineData(42, "CLI-0042", "PRJ-0042", "WI-0042", "TE-0042")]
    [InlineData(12345, "CLI-12345", "PRJ-12345", "WI-12345", "TE-12345")]
    public void Public_references_are_stable_and_category_specific(int number, string client, string project, string workItem, string timeEntry)
    {
        Assert.Equal(client, PublicReference.Client(number));
        Assert.Equal(project, PublicReference.Project(number));
        Assert.Equal(workItem, PublicReference.WorkItem(number));
        Assert.Equal(timeEntry, PublicReference.TimeEntry(number));
    }

    [Theory]
    [InlineData("PRJ-0007", "PRJ", 7)]
    [InlineData("wi 42", "WI", 42)]
    [InlineData("#9", "TE", 9)]
    [InlineData("12", "TE", 12)]
    public void Public_references_can_be_parsed_for_search(string input, string prefix, int expected)
    {
        Assert.True(PublicReference.TryParse(input, prefix, out var actual));
        Assert.Equal(expected, actual);
    }
}
