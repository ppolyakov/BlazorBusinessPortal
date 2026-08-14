using BusinessPortal.Application;
using BusinessPortal.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BusinessPortal.Infrastructure;

public sealed class DemoDataSeeder(
    ApplicationDbContext db,
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IConfiguration configuration)
{
    private const long ResetAdvisoryLockId = 8_615_2026;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("SeedDemoData"))
        {
            return;
        }

        var password = GetDemoPassword();
        foreach (var role in new[] { PortalRoles.Administrator, PortalRoles.Manager, PortalRoles.Employee })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)), $"create role {role}");
            }
        }

        var organization = await EnsureOrganizationAsync("Northstar Studio", "northstar", cancellationToken);
        var admin = await EnsureUserAsync("admin@northstar.demo", "Avery Admin", organization.Id, PortalRoles.Administrator, password);
        var managerOne = await EnsureUserAsync("manager@northstar.demo", "Laura Bennett", organization.Id, PortalRoles.Manager, password, "manager-female-01.png");
        var managerTwo = await EnsureUserAsync("manager2@northstar.demo", "Marcus Johnson", organization.Id, PortalRoles.Manager, password, "manager-male-02.png");
        var employeeOne = await EnsureUserAsync("employee@northstar.demo", "Daniel Kim", organization.Id, PortalRoles.Employee, password, "employee-male-01.png");
        var employeeTwo = await EnsureUserAsync("employee2@northstar.demo", "Priya Shah", organization.Id, PortalRoles.Employee, password, "employee-female-02.png");

        if (await db.Clients.AnyAsync(x => x.OrganizationId == organization.Id, cancellationToken))
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var nowUtc = DateTime.UtcNow;
        var clients = new[]
        {
            new Client { Number = 1, OrganizationId = organization.Id, Name = "Arcadia Retail", ContactName = "Olivia Chen", ContactEmail = "olivia@arcadia.example", ContactPhone = "+1 312 555 0142", Status = ClientStatus.Active },
            new Client { Number = 2, OrganizationId = organization.Id, Name = "Cedar Health", ContactName = "Lucas Reed", ContactEmail = "lucas@cedar.example", ContactPhone = "+1 617 555 0198", Status = ClientStatus.Active },
            new Client { Number = 3, OrganizationId = organization.Id, Name = "Fjord Logistics", ContactName = "Mia Jensen", ContactEmail = "mia@fjord.example", ContactPhone = "+45 32 55 71 20", Status = ClientStatus.Active },
            new Client { Number = 4, OrganizationId = organization.Id, Name = "Helio Foods", ContactName = "Noah Brooks", ContactEmail = "noah@helio.example", ContactPhone = "+44 20 7946 0183", Status = ClientStatus.Inactive }
        };
        db.Clients.AddRange(clients);

        var projects = new[]
        {
            NewProject(organization.Id, clients[0].Id, "Retail Operations Portal", "ARC-OPS", ProjectStatus.Active, 420, today.AddMonths(-3), today.AddMonths(3)),
            NewProject(organization.Id, clients[0].Id, "Store Analytics", "ARC-BI", ProjectStatus.Planned, 180, today.AddDays(14), null),
            NewProject(organization.Id, clients[1].Id, "Patient Scheduling", "CDR-SCH", ProjectStatus.Active, 600, today.AddMonths(-4), today.AddMonths(2)),
            NewProject(organization.Id, clients[1].Id, "Security Review", "CDR-SEC", ProjectStatus.OnHold, 90, today.AddMonths(-2), today.AddMonths(1)),
            NewProject(organization.Id, clients[2].Id, "Fleet Dashboard", "FJD-FLT", ProjectStatus.Active, 350, today.AddMonths(-1), today.AddMonths(4)),
            NewProject(organization.Id, clients[3].Id, "Vendor Integration", "HEL-VND", ProjectStatus.Completed, 120, today.AddMonths(-6), today.AddMonths(-1))
        };
        for (var index = 0; index < projects.Length; index++) projects[index].Number = index + 1;
        db.Projects.AddRange(projects);

        var team = new[] { managerOne, managerTwo, employeeOne, employeeTwo };
        var workItems = Enumerable.Range(0, WorkTitles.Length).Select(index => new WorkItem
        {
            Number = index + 1,
            OrganizationId = organization.Id,
            ProjectId = projects[index % 5].Id,
            Title = WorkTitles[index],
            Description = WorkDescriptions[index % WorkDescriptions.Length],
            Status = index % 6 == 0 ? WorkItemStatus.Done : index % 5 == 0 ? WorkItemStatus.InProgress : index % 11 == 0 ? WorkItemStatus.Blocked : WorkItemStatus.Open,
            Priority = index % 9 == 0 ? WorkItemPriority.High : index % 13 == 0 ? WorkItemPriority.Critical : WorkItemPriority.Normal,
            AssignedToUserId = team[index % team.Length].Id,
            DueDate = today.AddDays(index - 7),
            EstimatedHours = 3 + index % 14
        }).ToArray();
        db.WorkItems.AddRange(workItems);
        db.WorkItemActivities.AddRange(CreateWorkItemActivities(organization.Id, workItems, managerOne, nowUtc));

        var timeEntries = new List<TimeEntry>();
        for (var index = 0; index < 48; index++)
        {
            var owner = team[index % team.Length];
            var reviewer = owner.Id == managerOne.Id ? managerTwo : managerOne;
            var status = (index % 5) switch
            {
                0 => TimeEntryStatus.Draft,
                1 => TimeEntryStatus.Submitted,
                2 or 3 => TimeEntryStatus.Approved,
                _ => TimeEntryStatus.Returned
            };
            var eventTime = nowUtc.AddDays(-index).AddHours(-index % 8);
            timeEntries.Add(new TimeEntry
            {
                Number = index + 1,
                OrganizationId = organization.Id,
                ProjectId = projects[index % 5].Id,
                WorkItemId = workItems[index % workItems.Length].Id,
                UserId = owner.Id,
                WorkDate = today.AddDays(-(index * 2)),
                Hours = 2 + index % 7,
                Description = TimeDescriptions[index % TimeDescriptions.Length],
                Status = status,
                SubmittedAtUtc = status != TimeEntryStatus.Draft ? eventTime : null,
                SubmittedToUserId = status != TimeEntryStatus.Draft ? reviewer.Id : null,
                ReviewedAtUtc = status is TimeEntryStatus.Approved or TimeEntryStatus.Returned ? eventTime.AddHours(3) : null,
                ReviewedByUserId = status is TimeEntryStatus.Approved or TimeEntryStatus.Returned ? reviewer.Id : null,
                ReviewComment = status == TimeEntryStatus.Returned ? "Please add the outcome and link it to the delivery milestone." : null,
                CreatedAtUtc = eventTime.AddDays(-1),
                UpdatedAtUtc = eventTime
            });
        }
        db.TimeEntries.AddRange(timeEntries);
        db.TimeEntryActivities.AddRange(CreateTimeEntryActivities(organization.Id, timeEntries, managerOne, managerTwo));

        db.AuditEntries.AddRange(CreateAuditHistory(organization.Id, nowUtc, admin, managerOne, managerTwo, employeeOne, employeeTwo, clients, projects, workItems, timeEntries));
        db.Notifications.AddRange(CreateNotifications(organization.Id, nowUtc, admin, managerOne, managerTwo, employeeOne, employeeTwo, projects, workItems, timeEntries));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetAndSeedAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("SeedDemoData"))
        {
            throw new InvalidOperationException("SeedDemoData must be enabled before demo data can be reset.");
        }

        var executionStrategy = db.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({ResetAdvisoryLockId});", cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                TRUNCATE TABLE
                    "Notifications", "AuditEntries", "TimeEntryActivities", "WorkItemActivities", "TimeEntries", "WorkItems", "Projects", "Clients", "PublicNumberCounters",
                    "AspNetUserPasskeys", "AspNetUserTokens", "AspNetUserLogins", "AspNetUserClaims",
                    "AspNetUserRoles", "AspNetRoleClaims", "AspNetUsers", "AspNetRoles", "Organizations"
                RESTART IDENTITY CASCADE;
                """,
                cancellationToken);
            db.ChangeTracker.Clear();
            await SeedAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private string GetDemoPassword()
    {
        var password = configuration["DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("DemoPassword must be supplied when SeedDemoData is enabled.");
        }

        return password;
    }

    private async Task<Organization> EnsureOrganizationAsync(string name, string slug, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);
        if (organization is not null) return organization;
        organization = new Organization { Name = name, Slug = slug };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(cancellationToken);
        return organization;
    }

    private async Task<ApplicationUser> EnsureUserAsync(
        string email,
        string displayName,
        Guid organizationId,
        string role,
        string password,
        string? avatarFileName = null)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName,
                OrganizationId = organizationId
            };
            EnsureSucceeded(await userManager.CreateAsync(user, password), $"create user {email}");
        }
        if (!await userManager.IsInRoleAsync(user, role))
            EnsureSucceeded(await userManager.AddToRoleAsync(user, role), $"add {email} to {role}");

        if (user.AvatarImage is null && avatarFileName is not null)
        {
            var avatarPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", "avatars", avatarFileName);
            if (File.Exists(avatarPath))
            {
                user.AvatarImage = await File.ReadAllBytesAsync(avatarPath);
                user.AvatarContentType = "image/png";
                user.AvatarUpdatedAtUtc = DateTime.UtcNow;
                EnsureSucceeded(await userManager.UpdateAsync(user), $"set the demo avatar for {email}");
            }
        }

        return user;
    }

    private static IEnumerable<AuditEntry> CreateAuditHistory(
        Guid organizationId,
        DateTime nowUtc,
        ApplicationUser admin,
        ApplicationUser managerOne,
        ApplicationUser managerTwo,
        ApplicationUser employeeOne,
        ApplicationUser employeeTwo,
        Client[] clients,
        Project[] projects,
        WorkItem[] workItems,
        List<TimeEntry> timeEntries)
    {
        var actors = new[] { managerOne, employeeOne, managerTwo, employeeTwo, admin };
        var events = new (string Action, string EntityType, string EntityId, string Summary)[]
        {
            ("TimeEntryApproved", "TimeEntry", timeEntries[2].Id.ToString(), $"Approved {timeEntries[2].Hours:0.##} hours for Fleet Dashboard."),
            ("WorkItemUpdated", "WorkItem", workItems[15].Id.ToString(), $"Moved '{workItems[15].Title}' to In progress."),
            ("TimeEntrySubmitted", "TimeEntry", timeEntries[6].Id.ToString(), $"Submitted {timeEntries[6].Hours:0.##} hours for manager review."),
            ("ProjectUpdated", "Project", projects[4].Id.ToString(), "Updated Fleet Dashboard delivery dates."),
            ("WorkItemAssigned", "WorkItem", workItems[18].Id.ToString(), $"Assigned '{workItems[18].Title}' to {employeeTwo.DisplayName}."),
            ("ClientUpdated", "Client", clients[1].Id.ToString(), "Updated Cedar Health contact details."),
            ("TimeEntryReturned", "TimeEntry", timeEntries[9].Id.ToString(), "Returned a time entry for additional delivery detail."),
            ("ProjectCompleted", "Project", projects[5].Id.ToString(), "Completed Vendor Integration."),
            ("WorkItemCreated", "WorkItem", workItems[20].Id.ToString(), $"Created '{workItems[20].Title}'."),
            ("TimeEntryApproved", "TimeEntry", timeEntries[12].Id.ToString(), $"Approved {timeEntries[12].Hours:0.##} hours for Patient Scheduling."),
            ("ProjectCreated", "Project", projects[1].Id.ToString(), "Created Store Analytics."),
            ("ClientCreated", "Client", clients[3].Id.ToString(), "Created Helio Foods.")
        };

        return events.Select((item, index) => new AuditEntry
        {
            OrganizationId = organizationId,
            UserId = actors[index % actors.Length].Id,
            Action = item.Action,
            EntityType = item.EntityType,
            EntityId = item.EntityId,
            Summary = item.Summary,
            OccurredAtUtc = nowUtc.AddHours(-(index * 7 + 2))
        });
    }

    private static IEnumerable<WorkItemActivity> CreateWorkItemActivities(Guid organizationId, IEnumerable<WorkItem> items, ApplicationUser creator, DateTime nowUtc)
    {
        var index = 0;
        foreach (var item in items)
        {
            var createdAt = nowUtc.AddDays(-(30 - index)).AddHours(-2);
            yield return new WorkItemActivity { OrganizationId = organizationId, WorkItemId = item.Id, ActorUserId = creator.Id, Type = WorkItemActivityType.Created, ToStatus = WorkItemStatus.Open, Comment = "Work item created with delivery context.", OccurredAtUtc = createdAt };
            if (item.AssignedToUserId is not null)
                yield return new WorkItemActivity { OrganizationId = organizationId, WorkItemId = item.Id, ActorUserId = creator.Id, TargetUserId = item.AssignedToUserId, Type = WorkItemActivityType.Assigned, FromStatus = WorkItemStatus.Open, ToStatus = item.Status, Comment = "Assigned for delivery.", OccurredAtUtc = createdAt.AddMinutes(15) };
            if (item.Status == WorkItemStatus.Done)
                yield return new WorkItemActivity { OrganizationId = organizationId, WorkItemId = item.Id, ActorUserId = item.AssignedToUserId ?? creator.Id, TargetUserId = creator.Id, Type = WorkItemActivityType.Completed, FromStatus = WorkItemStatus.InProgress, ToStatus = WorkItemStatus.Done, Comment = "Delivery outcome completed and handed back for review.", OccurredAtUtc = createdAt.AddDays(3) };
            index++;
        }
    }

    private static IEnumerable<TimeEntryActivity> CreateTimeEntryActivities(
        Guid organizationId,
        IEnumerable<TimeEntry> entries,
        ApplicationUser managerOne,
        ApplicationUser managerTwo)
    {
        foreach (var entry in entries)
        {
            yield return NewTimeEntryActivity(
                organizationId,
                entry,
                entry.UserId,
                TimeEntryActivityType.Created,
                null,
                TimeEntryStatus.Draft,
                "Time entry created with delivery notes.",
                entry.CreatedAtUtc);

            if (entry.Status == TimeEntryStatus.Draft)
                continue;

            yield return NewTimeEntryActivity(
                organizationId,
                entry,
                entry.UserId,
                TimeEntryActivityType.Submitted,
                TimeEntryStatus.Draft,
                TimeEntryStatus.Submitted,
                "Submitted for manager review.",
                entry.SubmittedAtUtc ?? entry.UpdatedAtUtc,
                targetUserId: entry.SubmittedToUserId);

            var reviewer = entry.ReviewedByUserId == managerTwo.Id ? managerTwo : managerOne;
            if (entry.Status == TimeEntryStatus.Returned)
            {
                var rejectedAt = entry.ReviewedAtUtc ?? entry.UpdatedAtUtc;
                yield return NewTimeEntryActivity(
                    organizationId,
                    entry,
                    reviewer.Id,
                    TimeEntryActivityType.Returned,
                    TimeEntryStatus.Submitted,
                    TimeEntryStatus.Returned,
                    entry.ReviewComment,
                    rejectedAt,
                    entry.UserId);
                yield return NewTimeEntryActivity(
                    organizationId,
                    entry,
                    entry.UserId,
                    TimeEntryActivityType.Comment,
                    TimeEntryStatus.Returned,
                    TimeEntryStatus.Returned,
                    "Thanks for the review. I’ll add the delivery outcome and handoff reference before resubmitting.",
                    rejectedAt.AddMinutes(35),
                    targetUserId: entry.SubmittedToUserId);
            }
            else if (entry.Status == TimeEntryStatus.Approved)
            {
                yield return NewTimeEntryActivity(
                    organizationId,
                    entry,
                    reviewer.Id,
                    TimeEntryActivityType.Approved,
                    TimeEntryStatus.Submitted,
                    TimeEntryStatus.Approved,
                    "Reviewed against the delivery notes and approved.",
                    entry.ReviewedAtUtc ?? entry.UpdatedAtUtc,
                    entry.UserId);
            }
        }
    }

    private static TimeEntryActivity NewTimeEntryActivity(
        Guid organizationId,
        TimeEntry entry,
        string actorUserId,
        TimeEntryActivityType type,
        TimeEntryStatus? fromStatus,
        TimeEntryStatus? toStatus,
        string? comment,
        DateTime occurredAtUtc,
        string? targetUserId = null,
        string? targetLabel = null) => new()
        {
            OrganizationId = organizationId,
            TimeEntryId = entry.Id,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            TargetLabel = targetLabel,
            Type = type,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Comment = comment,
            OccurredAtUtc = occurredAtUtc
        };

    private static IEnumerable<Notification> CreateNotifications(
        Guid organizationId,
        DateTime nowUtc,
        ApplicationUser admin,
        ApplicationUser managerOne,
        ApplicationUser managerTwo,
        ApplicationUser employeeOne,
        ApplicationUser employeeTwo,
        Project[] projects,
        WorkItem[] workItems,
        List<TimeEntry> timeEntries)
    {
        return
        [
            NewNotification(organizationId, admin.Id, employeeOne.Id, NotificationType.TimeEntrySubmitted, "Timesheet ready for review", $"{employeeOne.DisplayName} submitted time for Retail Operations Portal.", $"/approvals?entry={timeEntries[1].Id}", "TimeEntry", timeEntries[1].Id, nowUtc.AddMinutes(-28)),
            NewNotification(organizationId, admin.Id, managerTwo.Id, NotificationType.ProjectCompleted, "Project completed", $"{managerTwo.DisplayName} completed Vendor Integration.", "/projects", "Project", projects[5].Id, nowUtc.AddHours(-5)),
            NewNotification(organizationId, managerOne.Id, employeeTwo.Id, NotificationType.TimeEntrySubmitted, "Timesheet ready for review", $"{employeeTwo.DisplayName} submitted time for Fleet Dashboard.", $"/approvals?entry={timeEntries[11].Id}", "TimeEntry", timeEntries[11].Id, nowUtc.AddMinutes(-47)),
            NewNotification(organizationId, managerTwo.Id, employeeOne.Id, NotificationType.TimeEntrySubmitted, "Timesheet ready for review", $"{employeeOne.DisplayName} submitted time for Patient Scheduling.", $"/approvals?entry={timeEntries[16].Id}", "TimeEntry", timeEntries[16].Id, nowUtc.AddHours(-2)),
            NewNotification(organizationId, employeeOne.Id, managerOne.Id, NotificationType.TimeEntryApproved, "Time approved", $"{managerOne.DisplayName} approved your Patient Scheduling entry.", $"/my-time?entry={timeEntries[2].Id}", "TimeEntry", timeEntries[2].Id, nowUtc.AddHours(-4)),
            NewNotification(organizationId, employeeOne.Id, managerTwo.Id, NotificationType.WorkItemAssigned, "New work item assigned", $"{managerTwo.DisplayName} assigned '{workItems[14].Title}' to you.", "/work-items", "WorkItem", workItems[14].Id, nowUtc.AddHours(-7)),
            NewNotification(organizationId, employeeTwo.Id, managerOne.Id, NotificationType.TimeEntryReturned, "Time entry needs changes", "Add the completed outcome before submitting again.", $"/my-time?entry={timeEntries[9].Id}", "TimeEntry", timeEntries[9].Id, nowUtc.AddHours(-9)),
            NewNotification(organizationId, employeeTwo.Id, managerTwo.Id, NotificationType.WorkItemAssigned, "New work item assigned", $"{managerTwo.DisplayName} assigned '{workItems[18].Title}' to you.", "/work-items", "WorkItem", workItems[18].Id, nowUtc.AddHours(-12))
        ];
    }

    private static Notification NewNotification(
        Guid organizationId,
        string recipientUserId,
        string actorUserId,
        NotificationType type,
        string title,
        string message,
        string targetUrl,
        string entityType,
        object entityId,
        DateTime createdAtUtc) => new()
        {
            OrganizationId = organizationId,
            RecipientUserId = recipientUserId,
            ActorUserId = actorUserId,
            Type = type,
            Title = title,
            Message = message,
            TargetUrl = targetUrl,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            CreatedAtUtc = createdAtUtc
        };

    private static Project NewProject(Guid organizationId, Guid clientId, string name, string code, ProjectStatus status, decimal budget, DateOnly start, DateOnly? end) =>
        new() { OrganizationId = organizationId, ClientId = clientId, Name = name, Code = code, Status = status, BudgetHours = budget, StartDate = start, EndDate = end, Description = "A realistic delivery engagement prepared for the Vela product demo." };

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Unable to {operation}: {string.Join("; ", result.Errors.Select(x => x.Description))}");
    }

    private static readonly string[] WorkTitles =
    [
        "Confirm discovery scope", "Model operational data", "Build client workspace", "Add project filters",
        "Implement time entry form", "Review access policies", "Create approval queue", "Verify tenant isolation",
        "Tune reporting query", "Design KPI cards", "Validate responsive layout", "Prepare Excel export",
        "Add audit timeline", "Review keyboard navigation", "Configure health checks", "Write migration guide",
        "Exercise manager workflow", "Exercise employee workflow", "Review error states", "Prepare portfolio captures",
        "Map release dependencies", "Finalize stakeholder review", "Document support handoff", "Plan launch retrospective"
    ];

    private static readonly string[] WorkDescriptions =
    [
        "Coordinate the next delivery milestone and capture the agreed outcome.",
        "Prepare a production-ready implementation with review notes and acceptance criteria.",
        "Validate the workflow with the project team and document any follow-up work.",
        "Review delivery risks, dependencies, and the plan for the next iteration."
    ];

    private static readonly string[] TimeDescriptions =
    [
        "Implemented the agreed workflow and completed peer review.",
        "Prepared the client review and incorporated feedback.",
        "Validated reporting data and documented the findings.",
        "Refined the delivery plan and resolved open dependencies.",
        "Validated the latest release candidate across key user journeys.",
        "Updated project documentation and acceptance criteria."
    ];
}
