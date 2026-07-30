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
        var current = new StubCurrentUser(new("user-1", seed.Organization1.Id, seed.Organization1.Name, "User One", new HashSet<string> { PortalRoles.Manager }));
        var service = new ClientService(fixture.CreateFactory(), current);
        var result = await service.SearchAsync(new(1, 100));
        Assert.Contains(result.Items, x => x.Id == seed.Client1.Id);
        Assert.DoesNotContain(result.Items, x => x.Id == seed.Client2.Id);
    }

    private static async Task<Seed> AddBaseDataAsync(ApplicationDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization1 = new Organization { Name = $"Org 1 {suffix}", Slug = $"org-1-{suffix}" };
        var organization2 = new Organization { Name = $"Org 2 {suffix}", Slug = $"org-2-{suffix}" };
        var client1 = new Client { OrganizationId = organization1.Id, Name = $"Client 1 {suffix}" };
        var client2 = new Client { OrganizationId = organization2.Id, Name = $"Client 2 {suffix}" };
        db.AddRange(organization1, organization2, client1, client2);
        await db.SaveChangesAsync();
        return new(organization1, organization2, client1, client2);
    }

    private static Project NewProject(Guid organizationId, Guid clientId, string code) => new()
    {
        OrganizationId = organizationId,
        ClientId = clientId,
        Name = code,
        Code = code,
        StartDate = new DateOnly(2026, 1, 1)
    };

    private sealed record Seed(Organization Organization1, Organization Organization2, Client Client1, Client Client2);
}

internal sealed class StubCurrentUser(CurrentUserInfo user) : ICurrentUser
{
    public Task<CurrentUserInfo> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(user);
}
