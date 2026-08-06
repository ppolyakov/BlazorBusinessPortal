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
