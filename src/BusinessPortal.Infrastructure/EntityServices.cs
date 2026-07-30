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
            query = query.Where(x => EF.Functions.ILike(x.Name, $"%{term}%") || (x.ContactName != null && EF.Functions.ILike(x.ContactName, $"%{term}%")));
        }

        query = (request.Sort?.ToLowerInvariant(), request.Descending) switch
        {
            ("status", false) => query.OrderBy(x => x.Status).ThenBy(x => x.Name),
            ("status", true) => query.OrderByDescending(x => x.Status).ThenBy(x => x.Name),
            ("name", true) => query.OrderByDescending(x => x.Name),
            _ => query.OrderBy(x => x.Name)
        };
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize)
            .Select(x => new ClientListItem(x.Id, x.Name, x.ContactName, x.ContactEmail, x.Status,
                db.Projects.Count(p => p.OrganizationId == user.OrganizationId && p.ClientId == x.Id)))
            .ToListAsync(cancellationToken);
        return new(items, count, request.SafePage, request.SafePageSize);
    }

    public async Task<ClientInput> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        return await db.Clients.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.Id == id)
            .Select(x => new ClientInput { Name = x.Name, ContactName = x.ContactName, ContactEmail = x.ContactEmail, Status = x.Status, Notes = x.Notes })
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
        entity.Status = input.Status;
        entity.Notes = input.Notes?.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(db, user, id is null ? "ClientCreated" : "ClientUpdated", nameof(Client), entity.Id, $"Client '{entity.Name}' saved.");
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
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
            query = query.Where(x => EF.Functions.ILike(x.project.Name, $"%{term}%") || EF.Functions.ILike(x.project.Code, $"%{term}%"));
        }

        query = request.Descending ? query.OrderByDescending(x => x.project.Name) : query.OrderBy(x => x.project.Name);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize)
            .Select(x => new ProjectListItem(x.project.Id, x.client.Id, x.client.Name, x.project.Name, x.project.Code, x.project.Status,
                x.project.BudgetHours, db.TimeEntries.Where(t => t.OrganizationId == user.OrganizationId && t.ProjectId == x.project.Id && t.Status == TimeEntryStatus.Approved).Sum(t => (decimal?)t.Hours) ?? 0,
                x.project.StartDate, x.project.EndDate))
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
        if (id is null)
        {
            entity = new Project { OrganizationId = user.OrganizationId, ClientId = input.ClientId, Name = input.Name.Trim(), Code = input.Code.Trim().ToUpperInvariant(), StartDate = input.StartDate };
            db.Projects.Add(entity);
        }
        else
        {
            entity = await db.Projects.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.Id == id, cancellationToken)
                ?? throw new ResourceNotFoundException("Project was not found.");
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

    public async Task<IReadOnlyList<LookupItem<Guid>>> LookupsAsync(bool timeEligibleOnly = false, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var query = db.Projects.AsNoTracking().Where(x => x.OrganizationId == user.OrganizationId);
        if (timeEligibleOnly)
        {
            query = query.Where(x => x.Status != ProjectStatus.Completed && x.Status != ProjectStatus.Archived);
        }
        return await query.OrderBy(x => x.Name).Select(x => new LookupItem<Guid>(x.Id, $"{x.Code} · {x.Name}")).ToListAsync(cancellationToken);
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
            query = query.Where(x => EF.Functions.ILike(x.item.Title, $"%{term}%"));
        }
        query = request.Descending ? query.OrderByDescending(x => x.item.DueDate).ThenBy(x => x.item.Title) : query.OrderBy(x => x.item.DueDate).ThenBy(x => x.item.Title);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize)
            .Select(x => new WorkItemListItem(x.item.Id, x.project.Id, x.project.Name, x.item.Title, x.item.Status, x.item.Priority, x.item.AssignedToUserId, x.assigned == null ? null : x.assigned.DisplayName, x.item.DueDate, x.item.EstimatedHours))
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

    public async Task<Guid> SaveAsync(Guid? id, WorkItemInput input, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        RequireManager(user);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        if (!await db.Projects.AnyAsync(x => x.OrganizationId == user.OrganizationId && x.Id == input.ProjectId, cancellationToken))
            throw new ResourceNotFoundException("Project was not found.");
        if (input.AssignedToUserId is not null && !await db.Users.AnyAsync(x => x.OrganizationId == user.OrganizationId && x.Id == input.AssignedToUserId, cancellationToken))
            throw new ResourceNotFoundException("Assigned user was not found.");

        WorkItem entity;
        if (id is null)
        {
            entity = new WorkItem { OrganizationId = user.OrganizationId, ProjectId = input.ProjectId, Title = input.Title.Trim() };
            db.WorkItems.Add(entity);
        }
        else
        {
            entity = await db.WorkItems.SingleOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.Id == id, cancellationToken)
                ?? throw new ResourceNotFoundException("Work item was not found.");
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
        AddAudit(db, user, id is null ? "WorkItemCreated" : "WorkItemUpdated", nameof(WorkItem), entity.Id, $"Work item '{entity.Title}' saved.");
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<IReadOnlyList<LookupItem<Guid>>> LookupsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        return await db.WorkItems.AsNoTracking().Where(x => x.OrganizationId == user.OrganizationId && x.ProjectId == projectId && x.Status != WorkItemStatus.Done)
            .OrderBy(x => x.Title).Select(x => new LookupItem<Guid>(x.Id, x.Title)).ToListAsync(cancellationToken);
    }
}
