using BusinessPortal.Application;
using BusinessPortal.Domain;
using BusinessPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class PersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Migrations_apply_to_clean_PostgreSQL()
    {
        await using var db = fixture.CreateContext();
        var migrations = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(migrations, migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Project_code_is_unique_inside_an_organization()
    {
        await using var db = fixture.CreateContext();
        var seed = await AddBaseDataAsync(db);
        db.Projects.Add(NewProject(seed.Organization1.Id, seed.Client1.Id, "DUP"));
        db.Projects.Add(NewProject(seed.Organization1.Id, seed.Client1.Id, "DUP"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Same_project_code_is_allowed_in_different_organizations()
    {
        await using var db = fixture.CreateContext();
        var seed = await AddBaseDataAsync(db);
        db.Projects.Add(NewProject(seed.Organization1.Id, seed.Client1.Id, "SHARED"));
        db.Projects.Add(NewProject(seed.Organization2.Id, seed.Client2.Id, "SHARED"));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Public_numbers_are_independent_per_entity_type_and_searchable()
    {
        await using var db = fixture.CreateContext();
        var seed = await AddBaseDataAsync(db);
        var firstProject = NewProject(seed.Organization1.Id, seed.Client1.Id, $"NUM-{Guid.NewGuid():N}"[..16]);
        var secondProject = NewProject(seed.Organization1.Id, seed.Client1.Id, $"NUM-{Guid.NewGuid():N}"[..16]);
        db.Projects.AddRange(firstProject, secondProject);
        await db.SaveChangesAsync();

        db.WorkItems.Add(new WorkItem
        {
            OrganizationId = seed.Organization1.Id,
            ProjectId = firstProject.Id,
            Title = "Numbered work item"
        });
        db.TimeEntries.Add(new TimeEntry
        {
            OrganizationId = seed.Organization1.Id,
            ProjectId = firstProject.Id,
            UserId = seed.User1.Id,
            WorkDate = new DateOnly(2026, 8, 13),
            Hours = 1,
            Description = "Numbered time entry"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var projectNumbers = await db.Projects.Where(x => x.OrganizationId == seed.Organization1.Id).OrderBy(x => x.Number).Select(x => x.Number).ToArrayAsync();
        var workItemNumbers = await db.WorkItems.Where(x => x.OrganizationId == seed.Organization1.Id).Select(x => x.Number).ToArrayAsync();
        var timeEntryNumbers = await db.TimeEntries.Where(x => x.OrganizationId == seed.Organization1.Id).Select(x => x.Number).ToArrayAsync();
        Assert.Collection(projectNumbers, number => Assert.Equal(1, number), number => Assert.Equal(2, number));
        Assert.Collection(workItemNumbers, number => Assert.Equal(1, number));
        Assert.Collection(timeEntryNumbers, number => Assert.Equal(1, number));

        var service = new ProjectService(fixture.CreateFactory(), ManagerFor(seed));
        var result = await service.SearchAsync(new(1, 20, "PRJ-0002"));
        var match = Assert.Single(result.Items);
        Assert.Equal(2, match.Number);
    }

    [Fact]
    public async Task Manager_can_bulk_delete_empty_projects()
    {
        await using var db = fixture.CreateContext();
        var seed = await AddBaseDataAsync(db);
        var first = NewProject(seed.Organization1.Id, seed.Client1.Id, $"DEL-{Guid.NewGuid():N}"[..16]);
        var second = NewProject(seed.Organization1.Id, seed.Client1.Id, $"DEL-{Guid.NewGuid():N}"[..16]);
        db.Projects.AddRange(first, second);
        await db.SaveChangesAsync();

        var service = new ProjectService(fixture.CreateFactory(), ManagerFor(seed));
        await service.DeleteAsync([first.Id, second.Id]);

        await using var verification = fixture.CreateContext();
        Assert.False(await verification.Projects.AnyAsync(x => x.Id == first.Id || x.Id == second.Id));
        Assert.Equal(2, await verification.AuditEntries.CountAsync(x => x.Action == "ProjectDeleted" && (x.EntityId == first.Id.ToString() || x.EntityId == second.Id.ToString())));
    }

    [Fact]
    public async Task Deleting_work_item_preserves_linked_time_entry_history()
    {
        await using var db = fixture.CreateContext();
        var seed = await AddBaseDataAsync(db);
        var project = NewProject(seed.Organization1.Id, seed.Client1.Id, $"WI-{Guid.NewGuid():N}"[..16]);
        var workItem = new WorkItem { OrganizationId = seed.Organization1.Id, ProjectId = project.Id, Title = "Disposable task" };
        var entry = new TimeEntry { OrganizationId = seed.Organization1.Id, ProjectId = project.Id, WorkItemId = workItem.Id, UserId = seed.User1.Id, WorkDate = new(2026, 8, 13), Hours = 2, Description = "Preserved history", Status = TimeEntryStatus.Approved };
        db.AddRange(project, workItem, entry);
        await db.SaveChangesAsync();

        var service = new WorkItemService(fixture.CreateFactory(), ManagerFor(seed));
        await service.DeleteAsync([workItem.Id]);

        await using var verification = fixture.CreateContext();
        Assert.False(await verification.WorkItems.AnyAsync(x => x.Id == workItem.Id));
        Assert.Null((await verification.TimeEntries.SingleAsync(x => x.Id == entry.Id)).WorkItemId);
    }

    [Fact]
    public async Task Client_service_never_returns_another_organizations_client()
    {
        await using var db = fixture.CreateContext();
        var seed = await AddBaseDataAsync(db);
        var current = ManagerFor(seed);
        var service = new ClientService(fixture.CreateFactory(), current);
        var result = await service.SearchAsync(new(1, 100));
        Assert.Contains(result.Items, x => x.Id == seed.Client1.Id);
        Assert.DoesNotContain(result.Items, x => x.Id == seed.Client2.Id);
    }

    [Fact]
    public async Task Client_service_assigns_a_public_number_and_persists_phone()
    {
        await using var db = fixture.CreateContext();
        var seed = await AddBaseDataAsync(db);
        var service = new ClientService(fixture.CreateFactory(), ManagerFor(seed));

        var id = await service.SaveAsync(null, new ClientInput
        {
            Name = "Phone contact",
            ContactName = "Jamie Reed",
            ContactEmail = "jamie@example.test",
            ContactPhone = "+1 415 555 0186"
        });

        var created = Assert.Single((await service.SearchAsync(new(1, 100))).Items, x => x.Id == id);
        var result = await service.SearchAsync(new(1, 100, created.Reference));
        var client = Assert.Single(result.Items, x => x.Id == id);
        Assert.True(client.Number > 0);
        Assert.Equal(PublicReference.Client(client.Number), client.Reference);
        Assert.Equal("+1 415 555 0186", client.ContactPhone);
    }

    [Fact]
    public async Task Client_service_deletes_an_unreferenced_client_and_records_an_audit_event()
    {
        await using var db = fixture.CreateContext();
        var seed = await AddBaseDataAsync(db);
        var service = new ClientService(fixture.CreateFactory(), ManagerFor(seed));

        await service.DeleteAsync(seed.Client1.Id);

        await using var verification = fixture.CreateContext();
        Assert.False(await verification.Clients.AnyAsync(x => x.Id == seed.Client1.Id));
        Assert.True(await verification.AuditEntries.AnyAsync(x =>
            x.OrganizationId == seed.Organization1.Id &&
            x.Action == "ClientDeleted" &&
            x.EntityId == seed.Client1.Id.ToString()));
    }

    [Fact]
    public async Task Client_service_bulk_deletes_unreferenced_clients_and_records_each_audit_event()
    {
        await using var db = fixture.CreateContext();
        var seed = await AddBaseDataAsync(db);
        var secondClient = new Client { OrganizationId = seed.Organization1.Id, Name = "Second removable client" };
        db.Clients.Add(secondClient);
        await db.SaveChangesAsync();
        var ids = new[] { seed.Client1.Id, secondClient.Id };
        var service = new ClientService(fixture.CreateFactory(), ManagerFor(seed));

        await service.DeleteAsync(ids);

        await using var verification = fixture.CreateContext();
        var entityIds = ids.Select(id => id.ToString()).ToArray();
        Assert.False(await verification.Clients.AnyAsync(x => ids.Contains(x.Id)));
        Assert.Equal(2, await verification.AuditEntries.CountAsync(x =>
            x.OrganizationId == seed.Organization1.Id
            && x.Action == "ClientDeleted"
            && entityIds.Contains(x.EntityId)));
    }

    [Fact]
    public async Task Client_service_refuses_to_delete_a_client_that_has_projects()
    {
        await using var db = fixture.CreateContext();
        var seed = await AddBaseDataAsync(db);
        db.Projects.Add(NewProject(seed.Organization1.Id, seed.Client1.Id, $"DEL-{Guid.NewGuid():N}"[..16]));
        await db.SaveChangesAsync();
        var service = new ClientService(fixture.CreateFactory(), ManagerFor(seed));

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(seed.Client1.Id));

        await using var verification = fixture.CreateContext();
        Assert.True(await verification.Clients.AnyAsync(x => x.Id == seed.Client1.Id));
    }

    private static async Task<Seed> AddBaseDataAsync(ApplicationDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization1 = new Organization { Name = $"Org 1 {suffix}", Slug = $"org-1-{suffix}" };
        var organization2 = new Organization { Name = $"Org 2 {suffix}", Slug = $"org-2-{suffix}" };
        var client1 = new Client { OrganizationId = organization1.Id, Name = $"Client 1 {suffix}" };
        var client2 = new Client { OrganizationId = organization2.Id, Name = $"Client 2 {suffix}" };
        var user1 = new ApplicationUser
        {
            Id = $"user-1-{suffix}",
            OrganizationId = organization1.Id,
            DisplayName = "User One",
            UserName = $"user-1-{suffix}@example.test",
            NormalizedUserName = $"USER-1-{suffix}@EXAMPLE.TEST"
        };
        db.AddRange(organization1, organization2, client1, client2, user1);
        await db.SaveChangesAsync();
        return new(organization1, organization2, client1, client2, user1);
    }

    private static StubCurrentUser ManagerFor(Seed seed) => new(new(
        seed.User1.Id,
        seed.Organization1.Id,
        seed.Organization1.Name,
        seed.User1.DisplayName,
        new HashSet<string> { PortalRoles.Manager }));

    private static Project NewProject(Guid organizationId, Guid clientId, string code) => new()
    {
        OrganizationId = organizationId,
        ClientId = clientId,
        Name = code,
        Code = code,
        StartDate = new DateOnly(2026, 1, 1)
    };

    private sealed record Seed(
        Organization Organization1,
        Organization Organization2,
        Client Client1,
        Client Client2,
        ApplicationUser User1);
}

internal sealed class StubCurrentUser(CurrentUserInfo user) : ICurrentUser
{
    public Task<CurrentUserInfo> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(user);
}
