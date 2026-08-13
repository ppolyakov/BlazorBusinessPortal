using BusinessPortal.Web;

namespace BusinessPortal.IntegrationTests;

public sealed class AuditDisplayTests
{
    [Theory]
    [InlineData("ClientCreated", "Client created", "success")]
    [InlineData("TimeEntryApproved", "Time entry approved", "success")]
    [InlineData("TimeEntryReturned", "Time entry returned", "danger")]
    [InlineData("TimeEntrySubmitted", "Time entry submitted", "warning")]
    [InlineData("ReportExported", "Report exported", "violet")]
    [InlineData("FutureAuditEvent", "Future audit event", "neutral")]
    public void Action_values_have_readable_labels_and_semantic_tones(string value, string label, string tone)
    {
        Assert.Equal(label, AuditDisplay.ActionLabel(value));
        Assert.Equal(tone, AuditDisplay.ActionTone(value));
    }

    [Theory]
    [InlineData("Client", "Client", "teal")]
    [InlineData("Project", "Project", "info")]
    [InlineData("WorkItem", "Work item", "warning")]
    [InlineData("TimeEntry", "Time entry", "violet")]
    [InlineData("FutureEntity", "Future entity", "neutral")]
    public void Entity_values_have_readable_labels_and_distinct_tones(string value, string label, string tone)
    {
        Assert.Equal(label, AuditDisplay.EntityLabel(value));
        Assert.Equal(tone, AuditDisplay.EntityTone(value));
    }
}
