namespace BusinessPortal.Domain;

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Client
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public int Number { get; set; }
    public required string Name { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public ClientStatus Status { get; set; } = ClientStatus.Active;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public int Number { get; set; }
    public Guid ClientId { get; set; }
    public required string Name { get; set; }
    public required string Code { get; set; }
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;
    public decimal BudgetHours { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public void ValidateDates()
    {
        if (EndDate < StartDate)
        {
            throw new DomainException("End date cannot be earlier than start date.");
        }
    }

    public bool AcceptsTime => Status is ProjectStatus.Planned or ProjectStatus.Active or ProjectStatus.OnHold;
}

public sealed class WorkItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public int Number { get; set; }
    public Guid ProjectId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Open;
    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Normal;
    public string? AssignedToUserId { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal EstimatedHours { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WorkItemActivity
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid WorkItemId { get; set; }
    public required string ActorUserId { get; set; }
    public string? TargetUserId { get; set; }
    public WorkItemActivityType Type { get; set; }
    public WorkItemStatus? FromStatus { get; set; }
    public WorkItemStatus? ToStatus { get; set; }
    public string? Comment { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class TimeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public int Number { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? WorkItemId { get; set; }
    public required string UserId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public required string Description { get; set; }
    public TimeEntryStatus Status { get; set; } = TimeEntryStatus.Draft;
    public DateTime? SubmittedAtUtc { get; set; }
    public string? SubmittedToUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? ReviewComment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public uint Version { get; set; }

    public void ValidateHours()
    {
        if (Hours is <= 0 or > 24)
        {
            throw new DomainException("Hours must be greater than zero and no more than 24.");
        }
    }

    public void Submit(DateTime nowUtc)
    {
        if (Status != TimeEntryStatus.Draft)
        {
            throw new DomainException("Only a draft time entry can be submitted.");
        }

        ValidateHours();
        Status = TimeEntryStatus.Submitted;
        SubmittedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void Approve(string reviewerUserId, DateTime nowUtc)
    {
        EnsureReviewAllowed(reviewerUserId);
        Status = TimeEntryStatus.Approved;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = nowUtc;
        ReviewComment = null;
        UpdatedAtUtc = nowUtc;
    }

    public void Return(string reviewerUserId, string comment, DateTime nowUtc)
    {
        EnsureReviewAllowed(reviewerUserId);
        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new DomainException("A return comment is required.");
        }

        Status = TimeEntryStatus.Returned;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = nowUtc;
        ReviewComment = comment.Trim();
        UpdatedAtUtc = nowUtc;
    }

    public void ReopenReturned(DateTime nowUtc)
    {
        if (Status != TimeEntryStatus.Returned)
        {
            throw new DomainException("Only a returned time entry can return to draft.");
        }

        Status = TimeEntryStatus.Draft;
        SubmittedAtUtc = null;
        SubmittedToUserId = null;
        ReviewedAtUtc = null;
        ReviewedByUserId = null;
        UpdatedAtUtc = nowUtc;
    }

    private void EnsureReviewAllowed(string reviewerUserId)
    {
        if (Status != TimeEntryStatus.Submitted)
        {
            throw new DomainException("Only a submitted time entry can be reviewed.");
        }

        if (string.Equals(UserId, reviewerUserId, StringComparison.Ordinal))
        {
            throw new DomainException("Users cannot review their own time entries.");
        }
    }
}

public sealed class TimeEntryActivity
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid TimeEntryId { get; set; }
    public required string ActorUserId { get; set; }
    public string? TargetUserId { get; set; }
    public string? TargetLabel { get; set; }
    public TimeEntryActivityType Type { get; set; }
    public TimeEntryStatus? FromStatus { get; set; }
    public TimeEntryStatus? ToStatus { get; set; }
    public string? Comment { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Summary { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Notification
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string RecipientUserId { get; set; }
    public string? ActorUserId { get; set; }
    public NotificationType Type { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public required string TargetUrl { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
}
