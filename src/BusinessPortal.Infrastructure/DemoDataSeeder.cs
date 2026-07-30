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
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("SeedDemoData"))
        {
            return;
        }

        var password = configuration["DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("DemoPassword must be supplied when SeedDemoData is enabled.");
        }

        foreach (var role in new[] { PortalRoles.Administrator, PortalRoles.Manager, PortalRoles.Employee })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                EnsureSucceeded(result, $"create role {role}");
            }
        }

        var northstar = await EnsureOrganizationAsync("Northstar Studio", "northstar", cancellationToken);
        var bluebird = await EnsureOrganizationAsync("Bluebird Labs", "bluebird", cancellationToken);
        var admin = await EnsureUserAsync("admin@northstar.demo", "Avery Admin", northstar.Id, PortalRoles.Administrator, password);
        var manager = await EnsureUserAsync("manager@northstar.demo", "Morgan Manager", northstar.Id, PortalRoles.Manager, password);
        var employee = await EnsureUserAsync("employee@northstar.demo", "Emery Employee", northstar.Id, PortalRoles.Employee, password);
        await EnsureUserAsync("manager@bluebird.demo", "Bailey Manager", bluebird.Id, PortalRoles.Manager, password);

        if (await db.Clients.AnyAsync(x => x.OrganizationId == northstar.Id, cancellationToken))
        {
            return;
        }

        var clients = new[]
        {
            new Client { OrganizationId = northstar.Id, Name = "Arcadia Retail", ContactName = "Olivia Chen", ContactEmail = "olivia@arcadia.example", Status = ClientStatus.Active },
            new Client { OrganizationId = northstar.Id, Name = "Cedar Health", ContactName = "Lucas Reed", ContactEmail = "lucas@cedar.example", Status = ClientStatus.Active },
            new Client { OrganizationId = northstar.Id, Name = "Fjord Logistics", ContactName = "Mia Jensen", ContactEmail = "mia@fjord.example", Status = ClientStatus.Active },
            new Client { OrganizationId = northstar.Id, Name = "Helio Foods", ContactName = "Noah Brooks", ContactEmail = "noah@helio.example", Status = ClientStatus.Inactive }
        };
        db.Clients.AddRange(clients);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var projects = new[]
        {
            NewProject(northstar.Id, clients[0].Id, "Retail Operations Portal", "ARC-OPS", ProjectStatus.Active, 420, today.AddMonths(-3), today.AddMonths(3)),
            NewProject(northstar.Id, clients[0].Id, "Store Analytics", "ARC-BI", ProjectStatus.Planned, 180, today.AddDays(14), null),
            NewProject(northstar.Id, clients[1].Id, "Patient Scheduling", "CDR-SCH", ProjectStatus.Active, 600, today.AddMonths(-4), today.AddMonths(2)),
            NewProject(northstar.Id, clients[1].Id, "Security Review", "CDR-SEC", ProjectStatus.OnHold, 90, today.AddMonths(-2), today.AddMonths(1)),
            NewProject(northstar.Id, clients[2].Id, "Fleet Dashboard", "FJD-FLT", ProjectStatus.Active, 350, today.AddMonths(-1), today.AddMonths(4)),
            NewProject(northstar.Id, clients[3].Id, "Vendor Integration", "HEL-VND", ProjectStatus.Completed, 120, today.AddMonths(-6), today.AddMonths(-1))
        };
        db.Projects.AddRange(projects);

        var workItems = Enumerable.Range(0, 20).Select(index => new WorkItem
        {
            OrganizationId = northstar.Id,
            ProjectId = projects[index % 5].Id,
            Title = WorkTitles[index],
            Description = "Demo work item showing a realistic delivery milestone.",
            Status = index % 5 == 0 ? WorkItemStatus.Done : index % 4 == 0 ? WorkItemStatus.InProgress : WorkItemStatus.Open,
            Priority = index % 7 == 0 ? WorkItemPriority.High : WorkItemPriority.Normal,
            AssignedToUserId = index % 3 == 0 ? manager.Id : employee.Id,
            DueDate = today.AddDays(index - 5),
            EstimatedHours = 4 + index % 12
        }).ToArray();
        db.WorkItems.AddRange(workItems);

        var timeEntries = new List<TimeEntry>();
        for (var index = 0; index < 32; index++)
        {
            var owner = index % 5 == 0 ? manager : employee;
            var status = (index % 4) switch
            {
                0 => TimeEntryStatus.Draft,
                1 => TimeEntryStatus.Submitted,
                2 => TimeEntryStatus.Approved,
                _ => TimeEntryStatus.Rejected
            };
            var entry = new TimeEntry
            {
                OrganizationId = northstar.Id,
                ProjectId = projects[index % 5].Id,
                WorkItemId = workItems[index % workItems.Length].Id,
                UserId = owner.Id,
                WorkDate = today.AddDays(-(index * 3)),
                Hours = 2 + index % 6,
                Description = $"Progress on {workItems[index % workItems.Length].Title.ToLowerInvariant()}.",
                Status = status,
                SubmittedAtUtc = status != TimeEntryStatus.Draft ? DateTime.UtcNow.AddDays(-index) : null,
                ReviewedAtUtc = status is TimeEntryStatus.Approved or TimeEntryStatus.Rejected ? DateTime.UtcNow.AddDays(-index).AddHours(2) : null,
                ReviewedByUserId = status is TimeEntryStatus.Approved or TimeEntryStatus.Rejected ? admin.Id : null,
                ReviewComment = status == TimeEntryStatus.Rejected ? "Please add more detail about the completed work." : null
            };
            timeEntries.Add(entry);
        }
        db.TimeEntries.AddRange(timeEntries);
        await db.SaveChangesAsync(cancellationToken);
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

    private async Task<ApplicationUser> EnsureUserAsync(string email, string displayName, Guid organizationId, string role, string password)
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
        return user;
    }

    private static Project NewProject(Guid organizationId, Guid clientId, string name, string code, ProjectStatus status, decimal budget, DateOnly start, DateOnly? end) =>
        new() { OrganizationId = organizationId, ClientId = clientId, Name = name, Code = code, Status = status, BudgetHours = budget, StartDate = start, EndDate = end, Description = "A portfolio demonstration project with realistic delivery data." };

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Unable to {operation}: {string.Join("; ", result.Errors.Select(x => x.Description))}");
    }

    private static readonly string[] WorkTitles =
    [
        "Confirm discovery scope", "Model operational data", "Build client workspace", "Add project filters",
        "Implement time entry form", "Review access policies", "Create approval queue", "Test tenant isolation",
        "Tune reporting query", "Design KPI cards", "Validate responsive layout", "Prepare Excel export",
        "Add audit timeline", "Review keyboard navigation", "Configure health checks", "Write migration guide",
        "Exercise manager workflow", "Exercise employee workflow", "Review error states", "Prepare portfolio captures"
    ];
}
