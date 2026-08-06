using System.Diagnostics;
using BusinessPortal.Application;
using BusinessPortal.Domain;
using BusinessPortal.Infrastructure;
using Xunit.Abstractions;

namespace BusinessPortal.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class PerformanceSmokeTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Performance")]
    public async Task Report_query_pages_an_increased_data_set()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N");
        var organization = new Organization { Name = "Performance Smoke", Slug = $"performance-{suffix}" };
        var manager = new ApplicationUser { Id = $"performance-user-{suffix}", UserName = $"performance-{suffix}", NormalizedUserName = $"PERFORMANCE-{suffix}", DisplayName = "Performance User", OrganizationId = organization.Id, SecurityStamp = suffix };
        var client = new Client { OrganizationId = organization.Id, Name = "Performance Client" };
        var project = new Project { OrganizationId = organization.Id, ClientId = client.Id, Name = "Increased Data Set", Code = $"PERF-{suffix[..6]}", Status = ProjectStatus.Active, StartDate = new DateOnly(2025, 1, 1) };
        db.AddRange(organization, manager, client, project);
        db.TimeEntries.AddRange(Enumerable.Range(0, 1_200).Select(index => new TimeEntry
        {
            OrganizationId = organization.Id,
            ProjectId = project.Id,
            UserId = manager.Id,
            WorkDate = new DateOnly(2025, 1, 1).AddDays(index % 365),
            Hours = 1 + index % 8,
            Description = $"Performance smoke row {index}",
            Status = TimeEntryStatus.Approved
        }));
        await db.SaveChangesAsync();

        var currentUser = new CurrentUserInfo(manager.Id, organization.Id, organization.Name, manager.DisplayName, new HashSet<string> { PortalRoles.Manager });
        var service = new ReportService(fixture.CreateFactory(), new StubCurrentUser(currentUser));
        var timer = Stopwatch.StartNew();
        var report = await service.GetAsync(new ReportFilter(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));
        timer.Stop();

        output.WriteLine($"1,200-row PostgreSQL report query: {timer.ElapsedMilliseconds} ms");
        Assert.Equal(1_200, report.Details.TotalCount);
        Assert.Equal(10, report.Details.Items.Count);
        Assert.Equal(10, report.Details.PageSize);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), $"Smoke query took {timer.Elapsed}.");
    }
}
