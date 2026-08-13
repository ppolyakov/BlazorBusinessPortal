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
            owner.AvatarImage == null ? null : "/avatars/" + owner.Id,
            entry.Number,
            project.Number,
            workItem == null ? null : workItem.Number);

    public async Task<PageResult<TimeEntryListItem>> MineAsync(PageRequest request, DateOnly? from = null, DateOnly? through = null, TimeEntryStatus? status = null, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entries = db.TimeEntries.AsNoTracking().Where(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId);
        if (from.HasValue) entries = entries.Where(x => x.WorkDate >= from);
        if (through.HasValue) entries = entries.Where(x => x.WorkDate <= through);
        if (status.HasValue) entries = entries.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var hasNumber = PublicReference.TryParse(term, "TE", out var number);
            entries = entries.Where(x => EF.Functions.ILike(x.Description, $"%{term}%")
                || db.Projects.Any(project => project.Id == x.ProjectId && EF.Functions.ILike(project.Name, $"%{term}%"))
                || db.WorkItems.Any(item => item.Id == x.WorkItemId && EF.Functions.ILike(item.Title, $"%{term}%"))
                || (hasNumber && x.Number == number));
        }
        var count = await entries.CountAsync(cancellationToken);
        entries = SortEntries(db, entries, request);
        var items = await ProjectRows(db, entries.Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize)).ToListAsync(cancellationToken);
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

    public async Task<TimeEntryDetails> GetDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entry = await (from timeEntry in db.TimeEntries.AsNoTracking()
                           join project in db.Projects.AsNoTracking() on timeEntry.ProjectId equals project.Id
                           join workItem in db.WorkItems.AsNoTracking() on timeEntry.WorkItemId equals workItem.Id into workItems
                           from workItem in workItems.DefaultIfEmpty()
                           join owner in db.Users.AsNoTracking() on timeEntry.UserId equals owner.Id
                           where timeEntry.OrganizationId == user.OrganizationId
                                 && timeEntry.Id == id
                                 && (timeEntry.UserId == user.UserId || user.CanManage)
                           select new
                           {
                               Entry = timeEntry,
                               ProjectName = project.Name,
                               ProjectNumber = project.Number,
                               WorkItemTitle = workItem == null ? null : workItem.Title,
                               WorkItemNumber = workItem == null ? (int?)null : workItem.Number,
                               OwnerName = owner.DisplayName,
                               OwnerAvatarUrl = owner.AvatarImage == null ? null : "/avatars/" + owner.Id
                           }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ResourceNotFoundException("Time entry was not found.");

        var activities = await (from activity in db.TimeEntryActivities.AsNoTracking()
                                join actor in db.Users.AsNoTracking() on activity.ActorUserId equals actor.Id
                                join target in db.Users.AsNoTracking() on activity.TargetUserId equals target.Id into targets
                                from target in targets.DefaultIfEmpty()
                                where activity.OrganizationId == user.OrganizationId && activity.TimeEntryId == id
                                orderby activity.OccurredAtUtc, activity.Id
                                select new TimeEntryActivityItem(
                                    activity.Id,
                                    activity.Type,
                                    actor.Id,
                                    actor.DisplayName,
                                    actor.AvatarImage == null ? null : "/avatars/" + actor.Id,
                                    target == null ? activity.TargetLabel : target.DisplayName,
                                    activity.FromStatus,
                                    activity.ToStatus,
                                    activity.Comment,
                                    activity.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        var historical = entry.Entry.Status == TimeEntryStatus.Approved;
        return new TimeEntryDetails(
            entry.Entry.Id,
            entry.Entry.Number,
            entry.ProjectName,
            entry.ProjectNumber,
            entry.WorkItemTitle,
            entry.WorkItemNumber,
            entry.Entry.UserId,
            entry.OwnerName,
            entry.OwnerAvatarUrl,
            entry.Entry.WorkDate,
            entry.Entry.Hours,
            entry.Entry.Description,
            entry.Entry.Status,
            entry.Entry.Version,
            historical,
            !historical,
            entry.Entry.UserId == user.UserId && entry.Entry.Status == TimeEntryStatus.Draft,
            entry.Entry.UserId == user.UserId && entry.Entry.Status == TimeEntryStatus.Draft,
            entry.Entry.UserId == user.UserId && entry.Entry.Status == TimeEntryStatus.Draft,
            entry.Entry.UserId == user.UserId && entry.Entry.Status == TimeEntryStatus.Returned,
            user.CanManage && entry.Entry.UserId != user.UserId && entry.Entry.Status == TimeEntryStatus.Submitted
                && (user.IsInRole(PortalRoles.Administrator) || entry.Entry.SubmittedToUserId == user.UserId),
            activities);
    }

    public async Task AddCommentAsync(Guid id, string comment, CancellationToken cancellationToken = default)
    {
        var normalizedComment = comment.Trim();
        if (string.IsNullOrWhiteSpace(normalizedComment))
            throw new ConflictException("Enter a comment before posting.");
        if (normalizedComment.Length > 1000)
            throw new ConflictException("Comments cannot be longer than 1,000 characters.");

        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entry = await db.TimeEntries.SingleOrDefaultAsync(
            x => x.OrganizationId == user.OrganizationId
                 && x.Id == id
                 && (x.UserId == user.UserId || user.IsInRole(PortalRoles.Administrator) || x.SubmittedToUserId == user.UserId),
            cancellationToken)
            ?? throw new ResourceNotFoundException("Time entry was not found.");
        if (entry.Status == TimeEntryStatus.Approved)
            throw new ConflictException("Historical time entries are read-only.");

        AddActivity(
            db,
            entry,
            user.UserId,
            TimeEntryActivityType.Comment,
            entry.Status,
            entry.Status,
            normalizedComment,
            user.UserId == entry.UserId ? entry.SubmittedToUserId : entry.UserId);

        if (user.UserId == entry.UserId)
        {
            if (entry.SubmittedToUserId is not null)
                NotificationWriter.ToUser(db, user.OrganizationId, entry.SubmittedToUserId, user.UserId, NotificationType.TimeEntryCommented, "New time entry comment", $"{user.DisplayName} added a comment to a {entry.Hours:0.##}h time entry.", $"/approvals?entry={entry.Id}", nameof(TimeEntry), entry.Id);
        }
        else
        {
            NotificationWriter.ToUser(
                db,
                user.OrganizationId,
                entry.UserId,
                user.UserId,
                NotificationType.TimeEntryCommented,
                "New time entry comment",
                $"{user.DisplayName} commented on your {entry.Hours:0.##}h time entry.",
                $"/my-time?entry={entry.Id}",
                nameof(TimeEntry),
                entry.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
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
            AddActivity(db, entity, user.UserId, TimeEntryActivityType.Created, null, TimeEntryStatus.Draft, "Time entry created.");
        }
        else
        {
            entity = await db.TimeEntries.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId && x.Id == id, cancellationToken)
                ?? throw new ResourceNotFoundException("Time entry was not found.");
            if (entity.Status != TimeEntryStatus.Draft)
                throw new ConflictException("Only draft time entries can be edited.");
            AddActivity(db, entity, user.UserId, TimeEntryActivityType.Updated, TimeEntryStatus.Draft, TimeEntryStatus.Draft, "Draft details updated.");
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

    public Task DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteDraftsAsync([id], cancellationToken);

    public async Task DeleteDraftsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var selectedIds = ids.Distinct().ToArray();
        if (selectedIds.Length == 0) return;
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entities = await db.TimeEntries
            .Where(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId && selectedIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (entities.Count != selectedIds.Length)
            throw new ResourceNotFoundException("One or more time entries were not found.");
        if (entities.Any(x => x.Status != TimeEntryStatus.Draft))
            throw new ConflictException("Only draft time entries can be deleted.");
        db.RemoveRange(entities);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SubmitAsync(Guid id, string managerUserId, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TimeEntries.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId && x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("Time entry was not found.");
        var project = await db.Projects.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.Id == entity.ProjectId)
            .Select(x => new { x.Code, x.Name })
            .SingleAsync(cancellationToken);
        var previousStatus = entity.Status;
        var manager = await GetManagerAsync(db, user.OrganizationId, managerUserId, cancellationToken);
        entity.Submit(DateTime.UtcNow);
        entity.SubmittedToUserId = manager.Id;
        AddActivity(db, entity, user.UserId, TimeEntryActivityType.Submitted, previousStatus, entity.Status, "Sent for review.", manager.Id);
        AddAudit(db, user, "TimeEntrySubmitted", nameof(TimeEntry), id, $"Submitted {entity.Hours:0.##} hours for review.");
        NotificationWriter.ToUser(db, user.OrganizationId, manager.Id, user.UserId, NotificationType.TimeEntrySubmitted, "Time entry awaiting approval", $"{user.DisplayName} submitted {entity.Hours:0.##}h for {project.Code} · {project.Name} on {entity.WorkDate:MMM d}.", $"/approvals?entry={entity.Id}", nameof(TimeEntry), entity.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReopenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TimeEntries.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.UserId == user.UserId && x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("Time entry was not found.");
        var previousStatus = entity.Status;
        entity.ReopenReturned(DateTime.UtcNow);
        AddActivity(db, entity, user.UserId, TimeEntryActivityType.Reopened, previousStatus, entity.Status, "Returned to draft to address reviewer feedback.");
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PageResult<TimeEntryListItem>> ApprovalsAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entries = db.TimeEntries.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.Status == TimeEntryStatus.Submitted && x.UserId != user.UserId);
        if (!user.IsInRole(PortalRoles.Administrator)) entries = entries.Where(x => x.SubmittedToUserId == user.UserId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var hasNumber = PublicReference.TryParse(term, "TE", out var number);
            entries = entries.Where(x => EF.Functions.ILike(x.Description, $"%{term}%")
                || db.Projects.Any(project => project.Id == x.ProjectId && EF.Functions.ILike(project.Name, $"%{term}%"))
                || db.Users.Any(owner => owner.Id == x.UserId && EF.Functions.ILike(owner.DisplayName, $"%{term}%"))
                || (hasNumber && x.Number == number));
        }
        var count = await entries.CountAsync(cancellationToken);
        entries = SortEntries(db, entries, request);
        var items = await ProjectRows(db, entries.Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize)).ToListAsync(cancellationToken);
        return new(items, count, request.SafePage, request.SafePageSize);
    }

    private static IQueryable<TimeEntry> SortEntries(ApplicationDbContext db, IQueryable<TimeEntry> entries, PageRequest request) =>
        (request.Sort?.ToLowerInvariant(), request.Descending) switch
        {
            ("number", true) => entries.OrderByDescending(x => x.Number),
            ("number", false) => entries.OrderBy(x => x.Number),
            ("date", true) => entries.OrderByDescending(x => x.WorkDate).ThenByDescending(x => x.Number),
            ("date", false) => entries.OrderBy(x => x.WorkDate).ThenBy(x => x.Number),
            ("project", true) => entries.OrderByDescending(x => db.Projects.Where(project => project.Id == x.ProjectId).Select(project => project.Name).First()).ThenByDescending(x => x.Number),
            ("project", false) => entries.OrderBy(x => db.Projects.Where(project => project.Id == x.ProjectId).Select(project => project.Name).First()).ThenBy(x => x.Number),
            ("description", true) => entries.OrderByDescending(x => x.Description),
            ("description", false) => entries.OrderBy(x => x.Description),
            ("hours", true) => entries.OrderByDescending(x => x.Hours),
            ("hours", false) => entries.OrderBy(x => x.Hours),
            ("status", true) => entries.OrderByDescending(x => x.Status).ThenByDescending(x => x.Number),
            ("status", false) => entries.OrderBy(x => x.Status).ThenBy(x => x.Number),
            ("person", true) => entries.OrderByDescending(x => db.Users.Where(owner => owner.Id == x.UserId).Select(owner => owner.DisplayName).First()).ThenByDescending(x => x.Number),
            ("person", false) => entries.OrderBy(x => db.Users.Where(owner => owner.Id == x.UserId).Select(owner => owner.DisplayName).First()).ThenBy(x => x.Number),
            _ => entries.OrderBy(x => x.Number)
        };

    public Task ApproveAsync(Guid id, uint version, CancellationToken cancellationToken = default) =>
        ReviewAsync(id, version, null, true, cancellationToken);

    public async Task ApproveAsync(IReadOnlyCollection<TimeEntryVersion> entries, CancellationToken cancellationToken = default)
    {
        var selected = entries.DistinctBy(x => x.Id).ToArray();
        if (selected.Length == 0) return;
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var ids = selected.Select(x => x.Id).ToArray();
        var entities = await db.TimeEntries
            .Where(x => x.OrganizationId == user.OrganizationId && x.UserId != user.UserId && ids.Contains(x.Id)
                && (user.IsInRole(PortalRoles.Administrator) || x.SubmittedToUserId == user.UserId))
            .ToListAsync(cancellationToken);
        if (entities.Count != selected.Length)
            throw new ResourceNotFoundException("One or more time entries were not found.");
        if (entities.Any(x => x.Status != TimeEntryStatus.Submitted))
            throw new ConflictException("Only submitted time entries can be approved.");
        var versions = selected.ToDictionary(x => x.Id, x => x.Version);
        var projectNames = await db.Projects.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && entities.Select(entry => entry.ProjectId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        foreach (var entity in entities)
        {
            db.Entry(entity).Property(x => x.Version).OriginalValue = versions[entity.Id];
            var previousStatus = entity.Status;
            entity.Approve(user.UserId, DateTime.UtcNow);
            AddActivity(db, entity, user.UserId, TimeEntryActivityType.Approved, previousStatus, entity.Status, "Time entry approved and moved to history.", entity.UserId);
            AddAudit(db, user, "TimeEntryApproved", nameof(TimeEntry), entity.Id, "Time entry approved.");
            NotificationWriter.ToUser(db, user.OrganizationId, entity.UserId, user.UserId, NotificationType.TimeEntryApproved, "Time entry approved", $"{user.DisplayName} approved your {entity.Hours:0.##}h entry for {projectNames[entity.ProjectId]}.", $"/my-time?entry={entity.Id}", nameof(TimeEntry), entity.Id);
        }
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("One or more time entries were already reviewed. Refresh the approval queue."); }
    }

    public Task ReturnAsync(Guid id, uint version, string comment, CancellationToken cancellationToken = default) =>
        ReviewAsync(id, version, comment, false, cancellationToken);

    private async Task ReviewAsync(Guid id, uint version, string? comment, bool approve, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TimeEntries.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("Time entry was not found.");
        if (!user.IsInRole(PortalRoles.Administrator) && entity.SubmittedToUserId != user.UserId)
            throw new ForbiddenException("This time entry was submitted to another manager.");
        var projectName = await db.Projects.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.Id == entity.ProjectId)
            .Select(x => x.Name)
            .SingleAsync(cancellationToken);
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        var previousStatus = entity.Status;
        if (approve) entity.Approve(user.UserId, DateTime.UtcNow);
        else entity.Return(user.UserId, comment ?? "", DateTime.UtcNow);
        AddActivity(
            db,
            entity,
            user.UserId,
            approve ? TimeEntryActivityType.Approved : TimeEntryActivityType.Returned,
            previousStatus,
            entity.Status,
            approve ? "Time entry approved and moved to history." : entity.ReviewComment,
            entity.UserId);
        AddAudit(db, user, approve ? "TimeEntryApproved" : "TimeEntryReturned", nameof(TimeEntry), id, approve ? "Time entry approved." : "Time entry returned with reviewer feedback.");
        NotificationWriter.ToUser(
            db,
            user.OrganizationId,
            entity.UserId,
            user.UserId,
            approve ? NotificationType.TimeEntryApproved : NotificationType.TimeEntryReturned,
            approve ? "Time entry approved" : "Time entry needs changes",
            approve
                ? $"{user.DisplayName} approved your {entity.Hours:0.##}h entry for {projectName}."
                : $"{user.DisplayName} returned your {entity.Hours:0.##}h entry for {projectName}. Review feedback is available.",
            $"/my-time?entry={entity.Id}",
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

    private static void AddActivity(
        ApplicationDbContext db,
        TimeEntry entry,
        string actorUserId,
        TimeEntryActivityType type,
        TimeEntryStatus? fromStatus,
        TimeEntryStatus? toStatus,
        string? comment = null,
        string? targetUserId = null,
        string? targetLabel = null)
    {
        db.TimeEntryActivities.Add(new TimeEntryActivity
        {
            OrganizationId = entry.OrganizationId,
            TimeEntryId = entry.Id,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            TargetLabel = targetLabel,
            Type = type,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Comment = comment,
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private static async Task<ApplicationUser> GetManagerAsync(ApplicationDbContext db, Guid organizationId, string userId, CancellationToken cancellationToken)
    {
        var managerRoles = new[] { PortalRoles.Administrator, PortalRoles.Manager };
        return await (from account in db.Users
                      join userRole in db.UserRoles on account.Id equals userRole.UserId
                      join role in db.Roles on userRole.RoleId equals role.Id
                      where account.OrganizationId == organizationId && account.IsActive && account.Id == userId && managerRoles.Contains(role.Name!)
                      select account).FirstOrDefaultAsync(cancellationToken)
            ?? throw new ResourceNotFoundException("The selected manager was not found.");
    }
}
