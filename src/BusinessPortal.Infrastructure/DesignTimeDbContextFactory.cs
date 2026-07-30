using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessPortal.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=business_portal;Username=postgres;Password=postgres";
        var services = new ServiceCollection();
        services.Configure<IdentityOptions>(identity =>
            identity.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        var applicationServices = services.BuildServiceProvider();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3))
            .UseApplicationServiceProvider(applicationServices)
            .Options;
        return new ApplicationDbContext(options);
    }
}
