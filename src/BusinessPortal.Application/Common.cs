using System.ComponentModel.DataAnnotations;

namespace BusinessPortal.Application;

public static class PortalRoles
{
    public const string Administrator = nameof(Administrator);
    public const string Manager = nameof(Manager);
    public const string Employee = nameof(Employee);
    public const string Management = Administrator + "," + Manager;
}

public sealed class ForbiddenException(string message = "You do not have permission to perform this action.") : Exception(message);
public sealed class ResourceNotFoundException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);

public sealed record CurrentUserInfo(
    string UserId,
    Guid OrganizationId,
    string OrganizationName,
    string DisplayName,
    IReadOnlySet<string> Roles,
    string? AvatarUrl = null)
{
    public bool IsInRole(string role) => Roles.Contains(role);
    public bool CanManage => IsInRole(PortalRoles.Administrator) || IsInRole(PortalRoles.Manager);
}

public interface ICurrentUser
{
    Task<CurrentUserInfo> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record PageRequest(int Page = 1, int PageSize = 20, string? Search = null, string? Sort = null, bool Descending = false)
{
    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, 100);
}

public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed class ClientInput
{
    [Required, StringLength(160)]
    public string Name { get; set; } = "";
    [StringLength(120)]
    public string? ContactName { get; set; }
    [EmailAddress, StringLength(200)]
    public string? ContactEmail { get; set; }
    [Phone, StringLength(40)]
    public string? ContactPhone { get; set; }
    public Domain.ClientStatus Status { get; set; } = Domain.ClientStatus.Active;
    [StringLength(1000)]
    public string? Notes { get; set; }
}

public sealed class ProjectInput : IValidatableObject
{
    [Required]
    public Guid ClientId { get; set; }
    [Required, StringLength(160)]
    public string Name { get; set; } = "";
    [Required, StringLength(30), RegularExpression("^[A-Za-z0-9-]+$")]
    public string Code { get; set; } = "";
    [StringLength(2000)]
    public string? Description { get; set; }
    public Domain.ProjectStatus Status { get; set; } = Domain.ProjectStatus.Planned;
    [Range(0, 100000)]
    public decimal BudgetHours { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? EndDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate < StartDate)
        {
            yield return new("End date cannot be earlier than start date.", [nameof(EndDate)]);
        }
    }
}

public sealed class WorkItemInput
{
    [Required]
    public Guid ProjectId { get; set; }
    [Required, StringLength(200)]
    public string Title { get; set; } = "";
    [StringLength(2000)]
    public string? Description { get; set; }
    public Domain.WorkItemStatus Status { get; set; } = Domain.WorkItemStatus.Open;
    public Domain.WorkItemPriority Priority { get; set; } = Domain.WorkItemPriority.Normal;
    public string? AssignedToUserId { get; set; }
    public DateOnly? DueDate { get; set; }
    [Range(0, 1000)]
    public decimal EstimatedHours { get; set; }
}

public sealed class TimeEntryInput
{
    [Required]
    public Guid ProjectId { get; set; }
    public Guid? WorkItemId { get; set; }
    public DateOnly WorkDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Range(typeof(decimal), "0.01", "24")]
    public decimal Hours { get; set; }
    [Required, StringLength(500)]
    public string Description { get; set; } = "";
}

public sealed record ClientListItem(Guid Id, string Name, string? ContactName, string? ContactEmail, string? ContactPhone, Domain.ClientStatus Status, int ProjectCount, int Number = 0)
{
    public string Reference => PublicReference.Client(Number);
}
public sealed record ProjectListItem(Guid Id, Guid ClientId, string ClientName, string Name, string Code, Domain.ProjectStatus Status, decimal BudgetHours, decimal UsedHours, DateOnly StartDate, DateOnly? EndDate, int Number = 0)
{
    public string Reference => PublicReference.Project(Number);
}
public sealed record WorkItemListItem(Guid Id, Guid ProjectId, string ProjectName, string Title, Domain.WorkItemStatus Status, Domain.WorkItemPriority Priority, string? AssignedToUserId, string? AssignedToName, DateOnly? DueDate, decimal EstimatedHours, string? AssignedToAvatarUrl = null, int Number = 0, int ProjectNumber = 0)
{
    public string Reference => PublicReference.WorkItem(Number);
    public string ProjectReference => PublicReference.Project(ProjectNumber);
}
public sealed record WorkItemActivityItem(
    long Id,
    Domain.WorkItemActivityType Type,
    string ActorUserId,
    string ActorName,
    string? ActorAvatarUrl,
    string? TargetName,
    Domain.WorkItemStatus? FromStatus,
    Domain.WorkItemStatus? ToStatus,
    string? Comment,
    DateTime OccurredAtUtc);
public sealed record WorkItemDetails(
    Guid Id,
    bool CanManage,
    bool CanReturn,
    bool CanComment,
    IReadOnlyList<WorkItemActivityItem> Activities)
{
    public bool IsAwaitingManagerComment { get; init; }
    public bool IsAwaitingReply { get; init; }
}
public sealed record WorkItemReturnRequest(string ManagerUserId, string Comment);
public sealed record TimeEntryListItem(Guid Id, Guid ProjectId, string ProjectName, Guid? WorkItemId, string? WorkItemTitle, string UserId, string UserName, DateOnly WorkDate, decimal Hours, string Description, Domain.TimeEntryStatus Status, string? ReviewComment, uint Version, string? UserAvatarUrl = null, int Number = 0, int ProjectNumber = 0, int? WorkItemNumber = null)
{
    public string Reference => PublicReference.TimeEntry(Number);
    public string ProjectReference => PublicReference.Project(ProjectNumber);
    public string? WorkItemReference => WorkItemNumber.HasValue ? PublicReference.WorkItem(WorkItemNumber.Value) : null;
}
public sealed record TimeEntryActivityItem(
    long Id,
    Domain.TimeEntryActivityType Type,
    string ActorUserId,
    string ActorName,
    string? ActorAvatarUrl,
    string? TargetName,
    Domain.TimeEntryStatus? FromStatus,
    Domain.TimeEntryStatus? ToStatus,
    string? Comment,
    DateTime OccurredAtUtc);
public sealed record TimeEntryVersion(Guid Id, uint Version);
public sealed record TimeEntryDetails(
    Guid Id,
    int Number,
    string ProjectName,
    int ProjectNumber,
    string? WorkItemTitle,
    int? WorkItemNumber,
    string UserId,
    string UserName,
    string? UserAvatarUrl,
    DateOnly WorkDate,
    decimal Hours,
    string Description,
    Domain.TimeEntryStatus Status,
    uint Version,
    bool IsHistorical,
    bool CanComment,
    bool CanEdit,
    bool CanDelete,
    bool CanSubmit,
    bool CanReopen,
    bool CanReview,
    IReadOnlyList<TimeEntryActivityItem> Activities);
public sealed record LookupItem<T>(T Id, string Name, string? AvatarUrl = null);
public sealed record AuditListItem(long Id, string UserName, string Action, string EntityType, string EntityId, string Summary, DateTime OccurredAtUtc, string? UserId = null, string? UserAvatarUrl = null);
public sealed record AuditFilterOptions(IReadOnlyList<string> Actions, IReadOnlyList<string> EntityTypes);
public sealed record DashboardModel(int ActiveClients, int ActiveProjects, decimal MonthHours, int AwaitingApproval, IReadOnlyList<ChartItem> HoursByProject, IReadOnlyList<AuditListItem> RecentActivity, IReadOnlyList<WorkItemListItem> UpcomingWork);
public sealed record ChartItem(string Label, decimal Value);
public sealed record NotificationListItem(long Id, Domain.NotificationType Type, string Title, string Message, string TargetUrl, DateTime CreatedAtUtc, DateTime? ReadAtUtc, string? ActorName = null, string? ActorAvatarUrl = null)
{
    public bool IsRead => ReadAtUtc.HasValue;
}
public sealed record NotificationFeed(IReadOnlyList<NotificationListItem> Items, int UnreadCount);
public sealed class ReportFilter(DateOnly from, DateOnly to)
{
    public DateOnly From { get; set; } = from;
    public DateOnly To { get; set; } = to;
    public Guid? ClientId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? UserId { get; set; }
    public string? Search { get; set; }
    public string? Sort { get; set; }
    public bool Descending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
public sealed record ReportRow(DateOnly WorkDate, string ClientName, string ProjectName, string UserName, decimal Hours, string Description, Domain.TimeEntryStatus Status, string? UserId = null, string? UserAvatarUrl = null, int TimeEntryNumber = 0, int ProjectNumber = 0)
{
    public string Reference => PublicReference.TimeEntry(TimeEntryNumber);
    public string ProjectReference => PublicReference.Project(ProjectNumber);
}
public sealed record ReportModel(IReadOnlyList<ChartItem> Totals, PageResult<ReportRow> Details, decimal TotalHours);

public interface IClientService
{
    Task<PageResult<ClientListItem>> SearchAsync(PageRequest request, CancellationToken cancellationToken = default);
    Task<ClientInput> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid> SaveAsync(Guid? id, ClientInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

public interface IProjectService
{
    Task<PageResult<ProjectListItem>> SearchAsync(PageRequest request, Guid? clientId = null, Domain.ProjectStatus? status = null, CancellationToken cancellationToken = default);
    Task<ProjectInput> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid> SaveAsync(Guid? id, ProjectInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupItem<Guid>>> LookupsAsync(bool timeEligibleOnly = false, CancellationToken cancellationToken = default);
}

public interface IWorkItemService
{
    Task<PageResult<WorkItemListItem>> SearchAsync(PageRequest request, Guid? projectId = null, Domain.WorkItemStatus? status = null, Domain.WorkItemPriority? priority = null, CancellationToken cancellationToken = default);
    Task<WorkItemInput> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkItemDetails> GetDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid> SaveAsync(Guid? id, WorkItemInput input, CancellationToken cancellationToken = default);
    Task ReturnAsync(Guid id, string managerUserId, string comment, CancellationToken cancellationToken = default);
    Task AddCommentAsync(Guid id, string comment, CancellationToken cancellationToken = default);
    Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupItem<Guid>>> LookupsAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public interface ITimeEntryService
{
    Task<PageResult<TimeEntryListItem>> MineAsync(PageRequest request, DateOnly? from = null, DateOnly? through = null, Domain.TimeEntryStatus? status = null, CancellationToken cancellationToken = default);
    Task<TimeEntryInput> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TimeEntryDetails> GetDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddCommentAsync(Guid id, string comment, CancellationToken cancellationToken = default);
    Task<Guid> SaveDraftAsync(Guid? id, TimeEntryInput input, CancellationToken cancellationToken = default);
    Task DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteDraftsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
    Task SubmitAsync(Guid id, string managerUserId, CancellationToken cancellationToken = default);
    Task ReopenAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PageResult<TimeEntryListItem>> ApprovalsAsync(PageRequest request, CancellationToken cancellationToken = default);
    Task ApproveAsync(Guid id, uint version, CancellationToken cancellationToken = default);
    Task ApproveAsync(IReadOnlyCollection<TimeEntryVersion> entries, CancellationToken cancellationToken = default);
    Task ReturnAsync(Guid id, uint version, string comment, CancellationToken cancellationToken = default);
}

public interface IDashboardService
{
    Task<DashboardModel> GetAsync(CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task<NotificationFeed> GetAsync(int take = 10, CancellationToken cancellationToken = default);
    Task<string> MarkReadAsync(long id, CancellationToken cancellationToken = default);
    Task MarkAllReadAsync(CancellationToken cancellationToken = default);
}

public interface IReportService
{
    Task<ReportModel> GetAsync(ReportFilter filter, CancellationToken cancellationToken = default);
    Task<(byte[] Content, string FileName)> ExportAsync(ReportFilter filter, CancellationToken cancellationToken = default);
}

public interface IAuditService
{
    Task<PageResult<AuditListItem>> SearchAsync(PageRequest request, string? action = null, string? entityType = null, DateOnly? from = null, DateOnly? through = null, CancellationToken cancellationToken = default);
    Task<AuditFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
}

public interface IUserDirectory
{
    Task<IReadOnlyList<LookupItem<string>>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupItem<string>>> ListManagersAsync(CancellationToken cancellationToken = default);
}
