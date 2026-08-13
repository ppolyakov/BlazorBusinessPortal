using BusinessPortal.Domain;

namespace BusinessPortal.Domain.Tests;

public sealed class TimeEntryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(24.01)]
    public void Invalid_hours_are_rejected(decimal hours)
    {
        var entry = Create(hours: hours);
        Assert.Throws<DomainException>(entry.ValidateHours);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(8)]
    [InlineData(24)]
    public void Valid_hours_are_accepted(decimal hours)
    {
        var entry = Create(hours: hours);
        entry.ValidateHours();
    }

    [Fact]
    public void Draft_can_be_submitted()
    {
        var now = DateTime.UtcNow;
        var entry = Create();
        entry.Submit(now);
        Assert.Equal(TimeEntryStatus.Submitted, entry.Status);
        Assert.Equal(now, entry.SubmittedAtUtc);
    }

    [Fact]
    public void Submitted_entry_cannot_be_submitted_twice()
    {
        var entry = Create();
        entry.Submit(DateTime.UtcNow);
        Assert.Throws<DomainException>(() => entry.Submit(DateTime.UtcNow));
    }

    [Fact]
    public void Submitted_entry_can_be_approved_by_another_user()
    {
        var entry = Create();
        entry.Submit(DateTime.UtcNow);
        entry.Approve("reviewer", DateTime.UtcNow);
        Assert.Equal(TimeEntryStatus.Approved, entry.Status);
        Assert.Equal("reviewer", entry.ReviewedByUserId);
    }

    [Fact]
    public void User_cannot_approve_own_entry()
    {
        var entry = Create();
        entry.Submit(DateTime.UtcNow);
        Assert.Throws<DomainException>(() => entry.Approve(entry.UserId, DateTime.UtcNow));
    }

    [Fact]
    public void Return_requires_comment()
    {
        var entry = Create();
        entry.Submit(DateTime.UtcNow);
        Assert.Throws<DomainException>(() => entry.Return("reviewer", " ", DateTime.UtcNow));
    }

    [Fact]
    public void Returned_entry_can_return_to_draft_and_be_resubmitted()
    {
        var entry = Create();
        entry.Submit(DateTime.UtcNow);
        entry.Return("reviewer", "Add detail.", DateTime.UtcNow);
        entry.ReopenReturned(DateTime.UtcNow);
        entry.Submit(DateTime.UtcNow);
        Assert.Equal(TimeEntryStatus.Submitted, entry.Status);
        Assert.Null(entry.ReviewedByUserId);
    }

    [Fact]
    public void Approved_entry_cannot_return_to_draft()
    {
        var entry = Create();
        entry.Submit(DateTime.UtcNow);
        entry.Approve("reviewer", DateTime.UtcNow);
        Assert.Throws<DomainException>(() => entry.ReopenReturned(DateTime.UtcNow));
    }

    private static TimeEntry Create(decimal hours = 8) => new()
    {
        OrganizationId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        UserId = "owner",
        WorkDate = DateOnly.FromDateTime(DateTime.Today),
        Hours = hours,
        Description = "Completed implementation work."
    };
}
