using System.Text;

namespace BusinessPortal.Web;

public static class AuditDisplay
{
    public static string ActionLabel(string value) => value switch
    {
        "ClientCreated" => "Client created",
        "ClientUpdated" => "Client updated",
        "ClientDeleted" => "Client deleted",
        "ProjectCreated" => "Project created",
        "ProjectUpdated" => "Project updated",
        "ProjectCompleted" => "Project completed",
        "ProjectDeleted" => "Project deleted",
        "WorkItemCreated" => "Work item created",
        "WorkItemUpdated" => "Work item updated",
        "WorkItemAssigned" => "Work item assigned",
        "WorkItemDeleted" => "Work item deleted",
        "TimeEntrySubmitted" => "Time entry submitted",
        "TimeEntryApproved" => "Time entry approved",
        "TimeEntryReturned" or "TimeEntryRejected" => "Time entry returned",
        "WorkItemReturned" => "Work item returned",
        "ReportExported" => "Report exported",
        _ => Humanize(value)
    };

    public static string EntityLabel(string value) => value switch
    {
        "Client" => "Client",
        "Project" => "Project",
        "WorkItem" => "Work item",
        "TimeEntry" => "Time entry",
        "Report" => "Report",
        _ => Humanize(value)
    };

    public static string ActionTone(string value) => value switch
    {
        "ClientDeleted" or "ProjectDeleted" or "WorkItemDeleted" or "TimeEntryReturned" or "TimeEntryRejected" or "WorkItemReturned" => "danger",
        "ClientCreated" or "ProjectCreated" or "ProjectCompleted" or "WorkItemCreated" or "TimeEntryApproved" => "success",
        "ClientUpdated" or "ProjectUpdated" or "WorkItemUpdated" or "WorkItemAssigned" => "info",
        "TimeEntrySubmitted" => "warning",
        "ReportExported" => "violet",
        _ => "neutral"
    };

    public static string EntityTone(string value) => value switch
    {
        "Client" => "teal",
        "Project" => "info",
        "WorkItem" => "warning",
        "TimeEntry" => "violet",
        "Report" => "neutral",
        _ => "neutral"
    };

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0
                && char.IsUpper(current)
                && (char.IsLower(value[index - 1])
                    || index + 1 < value.Length && char.IsLower(value[index + 1])))
            {
                result.Append(' ');
                result.Append(char.ToLowerInvariant(current));
                continue;
            }

            result.Append(current);
        }

        return result.ToString();
    }
}
