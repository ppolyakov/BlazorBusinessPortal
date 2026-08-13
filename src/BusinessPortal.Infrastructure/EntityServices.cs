using BusinessPortal.Application;
using BusinessPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.Infrastructure;

internal abstract class PortalService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser)
{
    protected IDbContextFactory<ApplicationDbContext> Factory { get; } = factory;
    protected ICurrentUser CurrentUser { get; } = currentUser;

    protected static void RequireManager(CurrentUserInfo user)
    {
        if (!user.CanManage)
        {
            throw new ForbiddenException();
        }
    }

    protected static void AddAudit(ApplicationDbContext db, CurrentUserInfo user, string action, string entityType, object id, string summary)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            OrganizationId = user.OrganizationId,
            UserId = user.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = id.ToString() ?? "",
            Summary = summary.Length <= 300 ? summary : summary[..300],
            OccurredAtUtc = DateTime.UtcNow
        });
    }
}

internal sealed class ClientService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser)
    : PortalService(factory, currentUser), IClientService
{
    public async Task<PageResult<ClientListItem>> SearchAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var query = db.Clients.AsNoTracking().Where(x => x.OrganizationId == user.OrganizationId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var hasNumber = PublicReference.TryParse(term, "CLI", out var number);
            query = query.Where(x => EF.Functions.ILike(x.Name, $"%{term}%")
                || (x.ContactName != null && EF.Functions.ILike(x.ContactName, $"%{term}%"))
                || (x.ContactEmail != null && EF.Functions.ILike(x.ContactEmail, $"%{term}%"))
                || (x.ContactPhone != null && EF.Functions.ILike(x.ContactPhone, $"%{term}%"))
                || (hasNumber && x.Number == number));
        }

        query = (request.Sort?.ToLowerInvariant(), request.Descending) switch
        {
            ("number", false) => query.OrderBy(x => x.Number),
            ("number", true) => query.OrderByDescending(x => x.Number),
            ("contact", false) => query.OrderBy(x => x.ContactName).ThenBy(x => x.Name),
            ("contact", true) => query.OrderByDescending(x => x.ContactName).ThenBy(x => x.Name),
            ("email", false) => query.OrderBy(x => x.ContactEmail).ThenBy(x => x.Name),
            ("email", true) => query.OrderByDescending(x => x.ContactEmail).ThenBy(x => x.Name),
            ("phone", false) => query.OrderBy(x => x.ContactPhone).ThenBy(x => x.Name),
            ("phone", true) => query.OrderByDescending(x => x.ContactPhone).ThenBy(x => x.Name),
            ("projects", false) => query.OrderBy(x => db.Projects.Count(p => p.OrganizationId == user.OrganizationId && p.ClientId == x.Id)).ThenBy(x => x.Name),
            ("projects", true) => query.OrderByDescending(x => db.Projects.Count(p => p.OrganizationId == user.OrganizationId && p.ClientId == x.Id)).ThenBy(x => x.Name),
            ("status", false) => query.OrderBy(x => x.Status).ThenBy(x => x.Name),
            ("status", true) => query.OrderByDescending(x => x.Status).ThenBy(x => x.Name),
            ("name", true) => query.OrderByDescending(x => x.Name),
            _ => query.OrderBy(x => x.Number)
        };
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize)
            .Select(x => new ClientListItem(x.Id, x.Name, x.ContactName, x.ContactEmail, x.ContactPhone, x.Status,
                db.Projects.Count(p => p.OrganizationId == user.OrganizationId && p.ClientId == x.Id), x.Number))
            .ToListAsync(cancellationToken);
        return new(items, count, request.SafePage, request.SafePageSize);
    }

    public async Task<ClientInput> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        return await db.Clients.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.Id == id)
            .Select(x => new ClientInput { Name = x.Name, ContactName = x.ContactName, ContactEmail = x.ContactEmail, ContactPhone = x.ContactPhone, Status = x.Status, Notes = x.Notes })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ResourceNotFoundException("Client was not found.");
    }

    public async Task<Guid> SaveAsync(Guid? id, ClientInput input, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        Client entity;
        if (id is null)
        {
            entity = new Client { OrganizationId = user.OrganizationId, Name = input.Name.Trim() };
            db.Clients.Add(entity);
        }
        else
        {
            entity = await db.Clients.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.Id == id, cancellationToken)
                ?? throw new ResourceNotFoundException("Client was not found.");
        }

        entity.Name = input.Name.Trim();
        entity.ContactName = input.ContactName?.Trim();
        entity.ContactEmail = input.ContactEmail?.Trim();
        entity.ContactPhone = input.ContactPhone?.Trim();
        entity.Status = input.Status;
        entity.Notes = input.Notes?.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(db, user, id is null ? "ClientCreated" : "ClientUpdated", nameof(Client), entity.Id, $"Client '{entity.Name}' saved.");
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => DeleteAsync([id], cancellationToken);

    public async Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return;
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var distinctIds = ids.Distinct().ToArray();
        var entities = await db.Clients
            .Where(x => x.OrganizationId == user.OrganizationId && distinctIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (entities.Count != distinctIds.Length)
            throw new ResourceNotFoundException("One or more clients were not found.");
        if (await db.Projects.AnyAsync(
            x => x.OrganizationId == user.OrganizationId && distinctIds.Contains(x.ClientId),
            cancellationToken))
        {
            throw new ConflictException("Clients with projects cannot be deleted. Remove or reassign their projects first.");
        }

        db.Clients.RemoveRange(entities);
        foreach (var entity in entities)
            AddAudit(db, user, "ClientDeleted", nameof(Client), entity.Id, $"Client '{entity.Name}' deleted.");
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class ProjectService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser)
    : PortalService(factory, currentUser), IProjectService
{
    public async Task<PageResult<ProjectListItem>> SearchAsync(PageRequest request, Guid? clientId = null, ProjectStatus? status = null, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var query = from project in db.Projects.AsNoTracking()
                    join client in db.Clients.AsNoTracking() on project.ClientId equals client.Id
                    where project.OrganizationId == user.OrganizationId && client.OrganizationId == user.OrganizationId
                    select new { project, client };
        if (clientId.HasValue) query = query.Where(x => x.project.ClientId == clientId);
        if (status.HasValue) query = query.Where(x => x.project.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var hasNumber = PublicReference.TryParse(term, "PRJ", out var number);
            query = query.Where(x => EF.Functions.ILike(x.project.Name, $"%{term}%")
                || EF.Functions.ILike(x.project.Code, $"%{term}%")
                || EF.Functions.ILike(x.client.Name, $"%{term}%")
                || (hasNumber && x.project.Number == number));
        }

        query = (request.Sort?.ToLowerInvariant(), request.Descending) switch
        {
            ("number", true) => query.OrderByDescending(x => x.project.Number),
            ("number", false) => query.OrderBy(x => x.project.Number),
            ("client", true) => query.OrderByDescending(x => x.client.Name).ThenBy(x => x.project.Name),
            ("client", false) => query.OrderBy(x => x.client.Name).ThenBy(x => x.project.Name),
            ("code", true) => query.OrderByDescending(x => x.project.Code),
            ("code", false) => query.OrderBy(x => x.project.Code),
            ("status", true) => query.OrderByDescending(x => x.project.Status).ThenBy(x => x.project.Name),
            ("status", false) => query.OrderBy(x => x.project.Status).ThenBy(x => x.project.Name),
            ("budget", true) => query.OrderByDescending(x => x.project.BudgetHours),
            ("budget", false) => query.OrderBy(x => x.project.BudgetHours),
            ("used", true) => query.OrderByDescending(x => db.TimeEntries.Where(t => t.OrganizationId == user.OrganizationId && t.ProjectId == x.project.Id && t.Status == TimeEntryStatus.Approved).Sum(t => (decimal?)t.Hours) ?? 0),
            ("used", false) => query.OrderBy(x => db.TimeEntries.Where(t => t.OrganizationId == user.OrganizationId && t.ProjectId == x.project.Id && t.Status == TimeEntryStatus.Approved).Sum(t => (decimal?)t.Hours) ?? 0),
            ("start", true) => query.OrderByDescending(x => x.project.StartDate),
            ("start", false) => query.OrderBy(x => x.project.StartDate),
            ("end", true) => query.OrderByDescending(x => x.project.EndDate),
            ("end", false) => query.OrderBy(x => x.project.EndDate),
            ("name", true) => query.OrderByDescending(x => x.project.Name),
            _ => query.OrderBy(x => x.project.Number)
        };
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize)
            .Select(x => new ProjectListItem(x.project.Id, x.client.Id, x.client.Name, x.project.Name, x.project.Code, x.project.Status,
                x.project.BudgetHours, db.TimeEntries.Where(t => t.OrganizationId == user.OrganizationId && t.ProjectId == x.project.Id && t.Status == TimeEntryStatus.Approved).Sum(t => (decimal?)t.Hours) ?? 0,
                x.project.StartDate, x.project.EndDate, x.project.Number))
            .ToListAsync(cancellationToken);
        return new(items, count, request.SafePage, request.SafePageSize);
    }

    public async Task<ProjectInput> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking().Where(x => x.OrganizationId == user.OrganizationId && x.Id == id)
            .Select(x => new ProjectInput { ClientId = x.ClientId, Name = x.Name, Code = x.Code, Description = x.Description, Status = x.Status, BudgetHours = x.BudgetHours, StartDate = x.StartDate, EndDate = x.EndDate })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ResourceNotFoundException("Project was not found.");
    }

    public async Task<Guid> SaveAsync(Guid? id, ProjectInput input, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        if (!await db.Clients.AnyAsync(x => x.OrganizationId == user.OrganizationId && x.Id == input.ClientId, cancellationToken))
        {
            throw new ResourceNotFoundException("Client was not found.");
        }

        Project entity;
        ProjectStatus? previousStatus = null;
        if (id is null)
        {
            entity = new Project { OrganizationId = user.OrganizationId, ClientId = input.ClientId, Name = input.Name.Trim(), Code = input.Code.Trim().ToUpperInvariant(), StartDate = input.StartDate };
            db.Projects.Add(entity);
        }
        else
        {
            entity = await db.Projects.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.Id == id, cancellationToken)
                ?? throw new ResourceNotFoundException("Project was not found.");
            previousStatus = entity.Status;
        }

        entity.ClientId = input.ClientId;
        entity.Name = input.Name.Trim();
        entity.Code = input.Code.Trim().ToUpperInvariant();
        entity.Description = input.Description?.Trim();
        entity.Status = input.Status;
        entity.BudgetHours = input.BudgetHours;
        entity.StartDate = input.StartDate;
        entity.EndDate = input.EndDate;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.ValidateDates();
        AddAudit(db, user, id is null ? "ProjectCreated" : "ProjectUpdated", nameof(Project), entity.Id, $"Project '{entity.Code}' saved.");
        if (entity.Status == ProjectStatus.Completed && previousStatus != ProjectStatus.Completed)
        {
            await NotificationWriter.ToOrganizationAsync(
                db,
                user.OrganizationId,
                user.UserId,
                NotificationType.ProjectCompleted,
                "Project completed",
                $"{user.DisplayName} completed {entity.Code} · {entity.Name}.",
                "/projects",
                nameof(Project),
                entity.Id,
                cancellationToken);
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Projects_OrganizationId_Code", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new ConflictException("Project code must be unique within the organization.");
        }
        return entity.Id;
    }

    public async Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var selectedIds = ids.Distinct().ToArray();
        if (selectedIds.Length == 0) return;
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var projects = await db.Projects
            .Where(x => x.OrganizationId == user.OrganizationId && selectedIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (projects.Count != selectedIds.Length)
            throw new ResourceNotFoundException("One or more projects were not found.");
        if (await db.WorkItems.AnyAsync(x => x.OrganizationId == user.OrganizationId && selectedIds.Contains(x.ProjectId), cancellationToken)
            || await db.TimeEntries.AnyAsync(x => x.OrganizationId == user.OrganizationId && selectedIds.Contains(x.ProjectId), cancellationToken))
            throw new ConflictException("Projects with work items or time entries cannot be deleted. Archive them instead or remove their related records first.");

        foreach (var project in projects)
            AddAudit(db, user, "ProjectDeleted", nameof(Project), project.Id, $"Project '{project.Code}' deleted.");
        db.Projects.RemoveRange(projects);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupItem<Guid>>> LookupsAsync(bool timeEligibleOnly = false, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var query = db.Projects.AsNoTracking().Where(x => x.OrganizationId == user.OrganizationId);
        if (timeEligibleOnly)
        {
            query = query.Where(x => x.Status != ProjectStatus.Completed && x.Status != ProjectStatus.Archived);
        }
        var items = await query.OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Number, x.Code, x.Name })
            .ToListAsync(cancellationToken);
        return items.Select(x => new LookupItem<Guid>(x.Id, $"{PublicReference.Project(x.Number)} · {x.Code} · {x.Name}")).ToArray();
    }
}

internal sealed class WorkItemService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser)
    : PortalService(factory, currentUser), IWorkItemService
{
    public async Task<PageResult<WorkItemListItem>> SearchAsync(PageRequest request, Guid? projectId = null, WorkItemStatus? status = null, WorkItemPriority? priority = null, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var query = from item in db.WorkItems.AsNoTracking()
                    join project in db.Projects.AsNoTracking() on item.ProjectId equals project.Id
                    join assigned in db.Users.AsNoTracking() on item.AssignedToUserId equals assigned.Id into assignments
                    from assigned in assignments.DefaultIfEmpty()
                    where item.OrganizationId == user.OrganizationId && project.OrganizationId == user.OrganizationId
                    select new { item, project, assigned };
        if (projectId.HasValue) query = query.Where(x => x.item.ProjectId == projectId);
        if (status.HasValue) query = query.Where(x => x.item.Status == status);
        if (priority.HasValue) query = query.Where(x => x.item.Priority == priority);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var hasNumber = PublicReference.TryParse(term, "WI", out var number);
            query = query.Where(x => EF.Functions.ILike(x.item.Title, $"%{term}%")
                || EF.Functions.ILike(x.project.Name, $"%{term}%")
                || (hasNumber && x.item.Number == number));
        }
        query = (request.Sort?.ToLowerInvariant(), request.Descending) switch
        {
            ("number", true) => query.OrderByDescending(x => x.item.Number),
            ("number", false) => query.OrderBy(x => x.item.Number),
            ("project", true) => query.OrderByDescending(x => x.project.Name).ThenBy(x => x.item.Title),
            ("project", false) => query.OrderBy(x => x.project.Name).ThenBy(x => x.item.Title),
            ("title", true) => query.OrderByDescending(x => x.item.Title),
            ("title", false) => query.OrderBy(x => x.item.Title),
            ("status", true) => query.OrderByDescending(x => x.item.Status).ThenBy(x => x.item.Title),
            ("status", false) => query.OrderBy(x => x.item.Status).ThenBy(x => x.item.Title),
            ("priority", true) => query.OrderByDescending(x => x.item.Priority).ThenBy(x => x.item.Title),
            ("priority", false) => query.OrderBy(x => x.item.Priority).ThenBy(x => x.item.Title),
            ("assignee", true) => query.OrderByDescending(x => x.assigned == null ? null : x.assigned.DisplayName).ThenBy(x => x.item.Title),
            ("assignee", false) => query.OrderBy(x => x.assigned == null ? null : x.assigned.DisplayName).ThenBy(x => x.item.Title),
            ("estimate", true) => query.OrderByDescending(x => x.item.EstimatedHours),
            ("estimate", false) => query.OrderBy(x => x.item.EstimatedHours),
            ("due", true) => query.OrderByDescending(x => x.item.DueDate).ThenBy(x => x.item.Title),
            _ => query.OrderBy(x => x.item.Number)
        };
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize)
            .Select(x => new WorkItemListItem(
                x.item.Id,
                x.project.Id,
                x.project.Name,
                x.item.Title,
                x.item.Status,
                x.item.Priority,
                x.item.AssignedToUserId,
                x.assigned == null ? null : x.assigned.DisplayName,
                x.item.DueDate,
                x.item.EstimatedHours,
                x.assigned == null || x.assigned.AvatarImage == null ? null : "/avatars/" + x.assigned.Id,
                x.item.Number,
                x.project.Number))
            .ToListAsync(cancellationToken);
        return new(items, count, request.SafePage, request.SafePageSize);
    }

    public async Task<WorkItemInput> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        return await db.WorkItems.AsNoTracking().Where(x => x.OrganizationId == user.OrganizationId && x.Id == id)
            .Select(x => new WorkItemInput { ProjectId = x.ProjectId, Title = x.Title, Description = x.Description, Status = x.Status, Priority = x.Priority, AssignedToUserId = x.AssignedToUserId, DueDate = x.DueDate, EstimatedHours = x.EstimatedHours })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ResourceNotFoundException("Work item was not found.");
    }

    public async Task<WorkItemDetails> GetDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var item = await db.WorkItems.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.Id == id)
            .Select(x => new { x.Id, x.Status, x.AssignedToUserId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ResourceNotFoundException("Work item was not found.");
        var activities = await (from activity in db.WorkItemActivities.AsNoTracking()
                                join actor in db.Users.AsNoTracking() on activity.ActorUserId equals actor.Id
                                join targetUser in db.Users.AsNoTracking() on activity.TargetUserId equals targetUser.Id into targets
                                from target in targets.DefaultIfEmpty()
                                where activity.OrganizationId == user.OrganizationId && activity.WorkItemId == id
                                orderby activity.OccurredAtUtc, activity.Id
                                select new WorkItemActivityItem(
                                    activity.Id,
                                    activity.Type,
                                    actor.Id,
                                    actor.DisplayName,
                                    actor.AvatarImage == null ? null : "/avatars/" + actor.Id,
                                    target == null ? null : target.DisplayName,
                                    activity.FromStatus,
                                    activity.ToStatus,
                                    activity.Comment,
                                    activity.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        var historical = item.Status == WorkItemStatus.Done;
        var lastDiscussionActivity = activities.LastOrDefault(x => x.Type is WorkItemActivityType.Comment or WorkItemActivityType.Returned);
        var isAwaitingReply = !historical && lastDiscussionActivity?.ActorUserId == user.UserId;
        var isAssignedEmployee = !user.CanManage && !historical && item.AssignedToUserId == user.UserId;
        var hasManagerReply = isAssignedEmployee
                              && lastDiscussionActivity?.Type == WorkItemActivityType.Comment
                              && await IsManagerAsync(db, user.OrganizationId, lastDiscussionActivity.ActorUserId, cancellationToken);
        return new(item.Id, user.CanManage && !historical, isAssignedEmployee && hasManagerReply, !historical && !isAwaitingReply, activities)
        {
            IsAwaitingManagerComment = isAssignedEmployee && !hasManagerReply && !isAwaitingReply,
            IsAwaitingReply = isAwaitingReply
        };
    }

    public async Task ReturnAsync(Guid id, string managerUserId, string comment, CancellationToken cancellationToken = default)
    {
        var normalizedComment = comment.Trim();
        if (string.IsNullOrWhiteSpace(normalizedComment)) throw new ConflictException("Explain why the work item is being returned.");
        if (normalizedComment.Length > 1000) throw new ConflictException("Comments cannot be longer than 1,000 characters.");
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var item = await db.WorkItems.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("Work item was not found.");
        if (user.CanManage || item.AssignedToUserId != user.UserId || item.Status == WorkItemStatus.Done)
            throw new ForbiddenException("Only the assigned employee can return an active work item.");
        var lastDiscussionActivity = await db.WorkItemActivities.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId
                        && x.WorkItemId == id
                        && (x.Type == WorkItemActivityType.Comment || x.Type == WorkItemActivityType.Returned))
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.ActorUserId, x.Type })
            .FirstOrDefaultAsync(cancellationToken);
        if (lastDiscussionActivity?.Type != WorkItemActivityType.Comment
            || !await IsManagerAsync(db, user.OrganizationId, lastDiscussionActivity.ActorUserId, cancellationToken))
            throw new ConflictException("A manager must comment on the work item before it can be returned.");
        if (!await IsManagerAsync(db, user.OrganizationId, managerUserId, cancellationToken))
            throw new ResourceNotFoundException("The selected manager was not found.");
        var previousStatus = item.Status;
        item.AssignedToUserId = managerUserId;
        item.Status = WorkItemStatus.Open;
        item.UpdatedAtUtc = DateTime.UtcNow;
        AddActivity(db, item, user.UserId, WorkItemActivityType.Returned, previousStatus, item.Status, normalizedComment, managerUserId);
        AddAudit(db, user, "WorkItemReturned", nameof(WorkItem), item.Id, $"Work item '{item.Title}' returned to a manager.");
        NotificationWriter.ToUser(db, user.OrganizationId, managerUserId, user.UserId, NotificationType.WorkItemReturned, "Work item returned", $"{user.DisplayName} returned '{item.Title}' with feedback.", $"/work-items?item={item.Id}", nameof(WorkItem), item.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddCommentAsync(Guid id, string comment, CancellationToken cancellationToken = default)
    {
        var normalizedComment = comment.Trim();
        if (string.IsNullOrWhiteSpace(normalizedComment)) throw new ConflictException("Enter a comment before posting.");
        if (normalizedComment.Length > 1000) throw new ConflictException("Comments cannot be longer than 1,000 characters.");
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var item = await db.WorkItems.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("Work item was not found.");
        if (item.Status == WorkItemStatus.Done) throw new ConflictException("Historical work items are read-only.");
        var lastActorUserId = await db.WorkItemActivities.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId
                        && x.WorkItemId == id
                        && (x.Type == WorkItemActivityType.Comment || x.Type == WorkItemActivityType.Returned))
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => x.ActorUserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (lastActorUserId == user.UserId)
            throw new ConflictException("Wait for another participant to reply before adding another comment.");
        AddActivity(db, item, user.UserId, WorkItemActivityType.Comment, item.Status, item.Status, normalizedComment, item.AssignedToUserId == user.UserId ? null : item.AssignedToUserId);
        if (item.AssignedToUserId is not null && item.AssignedToUserId != user.UserId)
            NotificationWriter.ToUser(db, user.OrganizationId, item.AssignedToUserId, user.UserId, NotificationType.WorkItemCommented, "New work item comment", $"{user.DisplayName} commented on '{item.Title}'.", $"/work-items?item={item.Id}", nameof(WorkItem), item.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> SaveAsync(Guid? id, WorkItemInput input, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(
            x => x.OrganizationId == user.OrganizationId && x.Id == input.ProjectId,
            cancellationToken)
            ?? throw new ResourceNotFoundException("Project was not found.");
        if (input.AssignedToUserId is not null && !await db.Users.AnyAsync(x => x.OrganizationId == user.OrganizationId && x.Id == input.AssignedToUserId, cancellationToken))
            throw new ResourceNotFoundException("Assigned user was not found.");

        WorkItem entity;
        string? previousAssigneeUserId = null;
        WorkItemStatus? previousStatus = null;
        if (id is null)
        {
            entity = new WorkItem { OrganizationId = user.OrganizationId, ProjectId = input.ProjectId, Title = input.Title.Trim() };
            db.WorkItems.Add(entity);
            AddActivity(db, entity, user.UserId, WorkItemActivityType.Created, null, WorkItemStatus.Open, "Work item created.");
        }
        else
        {
            entity = await db.WorkItems.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.Id == id, cancellationToken)
                ?? throw new ResourceNotFoundException("Work item was not found.");
            if (entity.Status == WorkItemStatus.Done)
                throw new ConflictException("Completed work items are historical records and cannot be edited.");
            previousAssigneeUserId = entity.AssignedToUserId;
            previousStatus = entity.Status;
        }
        entity.ProjectId = input.ProjectId;
        entity.Title = input.Title.Trim();
        entity.Description = input.Description?.Trim();
        entity.Status = input.Status;
        entity.Priority = input.Priority;
        entity.AssignedToUserId = input.AssignedToUserId;
        entity.DueDate = input.DueDate;
        entity.EstimatedHours = input.EstimatedHours;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        if (entity.AssignedToUserId != previousAssigneeUserId && entity.AssignedToUserId is not null)
            AddActivity(db, entity, user.UserId, WorkItemActivityType.Assigned, previousStatus ?? entity.Status, entity.Status, "Work item assigned.", entity.AssignedToUserId);
        else if (previousStatus != WorkItemStatus.Done && entity.Status == WorkItemStatus.Done)
            AddActivity(db, entity, user.UserId, WorkItemActivityType.Completed, previousStatus, entity.Status, "Work item completed.");
        else if (id is not null)
            AddActivity(db, entity, user.UserId, WorkItemActivityType.Updated, previousStatus, entity.Status, "Work item details updated.");
        AddAudit(db, user, id is null ? "WorkItemCreated" : "WorkItemUpdated", nameof(WorkItem), entity.Id, $"Work item '{entity.Title}' saved.");
        if (entity.AssignedToUserId is not null
            && entity.AssignedToUserId != previousAssigneeUserId
            && entity.AssignedToUserId != user.UserId)
        {
            NotificationWriter.ToUser(
                db,
                user.OrganizationId,
                entity.AssignedToUserId,
                user.UserId,
                NotificationType.WorkItemAssigned,
                "New work item assigned",
                $"{user.DisplayName} assigned '{entity.Title}' in {project.Name} to you.",
                "/work-items",
                nameof(WorkItem),
                entity.Id);
        }
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var selectedIds = ids.Distinct().ToArray();
        if (selectedIds.Length == 0) return;
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var workItems = await db.WorkItems
            .Where(x => x.OrganizationId == user.OrganizationId && selectedIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (workItems.Count != selectedIds.Length)
            throw new ResourceNotFoundException("One or more work items were not found.");
        if (workItems.Any(x => x.Status == WorkItemStatus.Done))
            throw new ConflictException("Completed work items are historical records and cannot be deleted.");

        foreach (var workItem in workItems)
            AddAudit(db, user, "WorkItemDeleted", nameof(WorkItem), workItem.Id, $"Work item '{workItem.Title}' deleted.");
        db.WorkItems.RemoveRange(workItems);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupItem<Guid>>> LookupsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var items = await db.WorkItems.AsNoTracking().Where(x => x.OrganizationId == user.OrganizationId && x.ProjectId == projectId && x.Status != WorkItemStatus.Done)
            .OrderBy(x => x.Title).Select(x => new { x.Id, x.Number, x.Title }).ToListAsync(cancellationToken);
        return items.Select(x => new LookupItem<Guid>(x.Id, $"{PublicReference.WorkItem(x.Number)} · {x.Title}")).ToArray();
    }

    private static async Task<bool> IsManagerAsync(ApplicationDbContext db, Guid organizationId, string userId, CancellationToken cancellationToken)
    {
        var managerRoles = new[] { PortalRoles.Administrator, PortalRoles.Manager };
        return await (from account in db.Users
                      join userRole in db.UserRoles on account.Id equals userRole.UserId
                      join role in db.Roles on userRole.RoleId equals role.Id
                      where account.OrganizationId == organizationId && account.IsActive && account.Id == userId && managerRoles.Contains(role.Name!)
                      select account.Id).AnyAsync(cancellationToken);
    }

    private static void AddActivity(ApplicationDbContext db, WorkItem item, string actorUserId, WorkItemActivityType type, WorkItemStatus? fromStatus, WorkItemStatus? toStatus, string? comment = null, string? targetUserId = null)
    {
        db.WorkItemActivities.Add(new WorkItemActivity { OrganizationId = item.OrganizationId, WorkItemId = item.Id, ActorUserId = actorUserId, TargetUserId = targetUserId, Type = type, FromStatus = fromStatus, ToStatus = toStatus, Comment = comment, OccurredAtUtc = DateTime.UtcNow });
    }
}
