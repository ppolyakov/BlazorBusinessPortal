using BusinessPortal.Domain;

namespace BusinessPortal.Domain.Tests;

public sealed class ProjectTests
{
    [Fact]
    public void End_date_cannot_precede_start_date()
    {
        var project = Create();
        project.EndDate = project.StartDate.AddDays(-1);
        Assert.Throws<DomainException>(project.ValidateDates);
    }

    [Fact]
    public void Equal_start_and_end_dates_are_valid()
    {
        var project = Create();
        project.EndDate = project.StartDate;
        project.ValidateDates();
    }

    [Theory]
    [InlineData(ProjectStatus.Completed)]
    [InlineData(ProjectStatus.Archived)]
    public void Closed_project_does_not_accept_time(ProjectStatus status)
    {
        var project = Create();
        project.Status = status;
        Assert.False(project.AcceptsTime);
    }

    private static Project Create() => new()
    {
        OrganizationId = Guid.NewGuid(),
        ClientId = Guid.NewGuid(),
        Name = "Portal",
        Code = "PORTAL",
        StartDate = new DateOnly(2026, 1, 1)
    };
}
