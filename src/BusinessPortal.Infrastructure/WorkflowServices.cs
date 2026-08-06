using BusinessPortal.Application;
using BusinessPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.Infrastructure;

internal sealed class TimeEntryService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser)
    : PortalService(factory, currentUser), ITimeEntryService
{
    private static IQueryable<TimeEntryListItem> ProjectRows(ApplicationDbContext db, IQueryable<TimeEntry> entries) =>
        from entry in entries
        join project in db.Projects.AsNoTracking() on entry.ProjectId equals project.Id
        join workItem in db.WorkItems.AsNoTracking() on entry.WorkItemId equals workItem.Id into workItems
        from workItem in workItems.DefaultIfEmpty()
        join owner in db.Users.AsNoTracking() on entry.UserId equals owner.Id
        select new TimeEntryListItem(entry.Id, project.Id, project.Name, entry.WorkItemId, workItem == null ? null : workItem.Title,
            entry.UserId, owner.DisplayName, entry.WorkDate, entry.Hours, entry.Description, entry.Status, entry.ReviewComment, entry.Version,
            owner.AvatarImage == null ? null : "/avatars/" + owner.Id);

    public async Task<PageResult<TimeEntryListItem>> MineAsync(PageRequest request, DateOnly? from = null, DateOnly? through = null, TimeEntryStatus? status = null, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entries = db.TimeEntries.AsNoTracking().Where(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId);
        if (from.HasValue) entries = entries.Where(x => x.WorkDate >= from);
        if (through.HasValue) entries = entries.Where(x => x.WorkDate <= through);
        if (status.HasValue) entries = entries.Where(x => x.Status == status);
        entries = entries.OrderByDescending(x => x.WorkDate).ThenByDescending(x => x.CreatedAtUtc);
        var count = await entries.CountAsync(cancellationToken);
        var items = await ProjectRows(db, entries).Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize).ToListAsync(cancellationToken);
        return new(items, count, request.SafePage, request.SafePageSize);
    }

    public async Task<TimeEntryInput> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        return await db.TimeEntries.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId && x.Id == id)
            .Select(x => new TimeEntryInput { ProjectId = x.ProjectId, WorkItemId = x.WorkItemId, WorkDate = x.WorkDate, Hours = x.Hours, Description = x.Description })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ResourceNotFoundException("Time entry was not found.");
    }

    public async Task<Guid> SaveDraftAsync(Guid? id, TimeEntryInput input, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.Id == input.ProjectId, cancellationToken)
            ?? throw new ResourceNotFoundException("Project was not found.");
        if (!project.AcceptsTime)
            throw new ConflictException("Completed or archived projects cannot receive new time entries.");
        if (input.WorkItemId.HasValue && !await db.WorkItems.AnyAsync(x => x.OrganizationId == user.OrganizationId && x.ProjectId == input.ProjectId && x.Id == input.WorkItemId, cancellationToken))
            throw new ResourceNotFoundException("Work item was not found.");

        TimeEntry entity;
        if (id is null)
        {
            entity = new TimeEntry { OrganizationId = user.OrganizationId, ProjectId = input.ProjectId, UserId = user.UserId, WorkDate = input.WorkDate, Hours = input.Hours, Description = input.Description.Trim() };
            db.TimeEntries.Add(entity);
        }
        else
        {
            entity = await db.TimeEntries.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId && x.Id == id, cancellationToken)
                ?? throw new ResourceNotFoundException("Time entry was not found.");
            if (entity.Status != TimeEntryStatus.Draft)
                throw new ConflictException("Only draft time entries can be edited.");
        }
        entity.ProjectId = input.ProjectId;
        entity.WorkItemId = input.WorkItemId;
        entity.WorkDate = input.WorkDate;
        entity.Hours = input.Hours;
        entity.Description = input.Description.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.ValidateHours();
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TimeEntries.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId && x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("Time entry was not found.");
        if (entity.Status != TimeEntryStatus.Draft)
            throw new ConflictException("Only draft time entries can be deleted.");
        db.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SubmitAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TimeEntries.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId && x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("Time entry was not found.");
        var project = await db.Projects.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.Id == entity.ProjectId)
            .Select(x => new { x.Code, x.Name })
            .SingleAsync(cancellationToken);
        entity.Submit(DateTime.UtcNow);
        AddAudit(db, user, "TimeEntrySubmitted", nameof(TimeEntry), id, $"Submitted {entity.Hours:0.##} hours for review.");
        await NotificationWriter.ToRolesAsync(
            db,
            user.OrganizationId,
            [PortalRoles.Administrator, PortalRoles.Manager],
            user.UserId,
            NotificationType.TimeEntrySubmitted,
            "Time entry awaiting approval",
            $"{user.DisplayName} submitted {entity.Hours:0.##}h for {project.Code} · {project.Name} on {entity.WorkDate:MMM d}.",
            "/approvals",
            nameof(TimeEntry),
            entity.Id,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReopenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TimeEntries.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId && x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("Time entry was not found.");
        entity.ReopenRejected(DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PageResult<TimeEntryListItem>> ApprovalsAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entries = db.TimeEntries.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.Status == TimeEntryStatus.Submitted && x.UserId != user.UserId)
            .OrderBy(x => x.SubmittedAtUtc);
        var count = await entries.CountAsync(cancellationToken);
        var items = await ProjectRows(db, entries).Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize).ToListAsync(cancellationToken);
        return new(items, count, request.SafePage, request.SafePageSize);
    }

    public Task ApproveAsync(Guid id, uint version, CancellationToken cancellationToken = default) =>
        ReviewAsync(id, version, null, true, cancellationToken);

    public Task RejectAsync(Guid id, uint version, string comment, CancellationToken cancellationToken = default) =>
        ReviewAsync(id, version, comment, false, cancellationToken);

    private async Task ReviewAsync(Guid id, uint version, string? comment, bool approve, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TimeEntries.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("Time entry was not found.");
        var projectName = await db.Projects.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.Id == entity.ProjectId)
            .Select(x => x.Name)
            .SingleAsync(cancellationToken);
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        if (approve) entity.Approve(user.UserId, DateTime.UtcNow);
        else entity.Reject(user.UserId, comment ?? "", DateTime.UtcNow);
        AddAudit(db, user, approve ? "TimeEntryApproved" : "TimeEntryRejected", nameof(TimeEntry), id, approve ? "Time entry approved." : "Time entry rejected with reviewer feedback.");
        NotificationWriter.ToUser(
            db,
            user.OrganizationId,
            entity.UserId,
            user.UserId,
            approve ? NotificationType.TimeEntryApproved : NotificationType.TimeEntryRejected,
            approve ? "Time entry approved" : "Time entry needs changes",
            approve
                ? $"{user.DisplayName} approved your {entity.Hours:0.##}h entry for {projectName}."
                : $"{user.DisplayName} returned your {entity.Hours:0.##}h entry for {projectName}. Review feedback is available.",
            "/my-time",
            nameof(TimeEntry),
            entity.Id);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This time entry was already reviewed. Refresh the approval queue.");
        }
    }
}
