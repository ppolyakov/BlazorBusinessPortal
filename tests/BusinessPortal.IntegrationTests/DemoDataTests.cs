using System.Globalization;
using BusinessPortal.Application;
using BusinessPortal.Domain;
using BusinessPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessPortal.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class DemoDataTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Reset_restores_complete_demo_baseline()
    {
        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().ResetAndSeedAsync();
        }

        await using (var modified = fixture.CreateContext())
        {
            var organizationId = await modified.Organizations.Select(x => x.Id).SingleAsync();
            modified.Clients.Add(new Client { OrganizationId = organizationId, Name = "Visitor edit" });
            await modified.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().ResetAndSeedAsync();
        }

        await using var verification = fixture.CreateContext();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        Assert.Equal(1, await verification.Organizations.CountAsync());
        Assert.Equal(5, await verification.Users.CountAsync());
        var demoPeople = await verification.Users
            .OrderBy(x => x.Email)
            .Select(x => $"{x.Email}|{x.DisplayName}")
            .ToArrayAsync();
        Assert.Equal(
            [
                "admin@northstar.demo|Avery Admin",
                "employee2@northstar.demo|Priya Shah",
                "employee@northstar.demo|Daniel Kim",
                "manager2@northstar.demo|Marcus Johnson",
                "manager@northstar.demo|Laura Bennett"
            ],
            demoPeople);
        Assert.Equal(2, await UsersInRoleAsync(verification, PortalRoles.Manager));
        Assert.Equal(2, await UsersInRoleAsync(verification, PortalRoles.Employee));
        Assert.Equal(1, await UsersInRoleAsync(verification, PortalRoles.Administrator));
        Assert.Equal(4, await verification.Clients.CountAsync());
        Assert.Equal(6, await verification.Projects.CountAsync());
        Assert.Equal(24, await verification.WorkItems.CountAsync());
        Assert.Equal(48, await verification.TimeEntries.CountAsync());
        Assert.True(await verification.TimeEntries.AnyAsync(x => x.WorkDate >= monthStart));
        Assert.True(await verification.AuditEntries.CountAsync() >= 10);
        Assert.True(await verification.Notifications.CountAsync() >= 8);
        Assert.False(await verification.Clients.AnyAsync(x => x.Name == "Visitor edit"));
    }

    [Theory]
    [InlineData("2026-08-13T02:30:00+00:00", 3, 30)]
    [InlineData("2026-08-13T03:00:00+00:00", 3, 1440)]
    [InlineData("2026-08-13T22:00:00+00:00", 3, 300)]
    public void Nightly_reset_delay_targets_next_configured_utc_hour(string now, int hourUtc, int expectedMinutes)
    {
        var delay = DemoResetHostedService.DelayUntilNextReset(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture), hourUtc);
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), delay);
    }

    private ServiceProvider BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
                ["SeedDemoData"] = "true",
                ["DemoPassword"] = "DemoPassword123!"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddIdentityCore<ApplicationUser>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<DemoDataSeeder>();
        return services.BuildServiceProvider();
    }

    private static Task<int> UsersInRoleAsync(ApplicationDbContext db, string roleName) =>
        (from userRole in db.UserRoles
         join role in db.Roles on userRole.RoleId equals role.Id
         where role.Name == roleName
         select userRole.UserId).CountAsync();
}
