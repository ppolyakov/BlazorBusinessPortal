using BusinessPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace BusinessPortal.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("business_portal_tests")
        .WithUsername("tests")
        .WithPassword("tests-password")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    public ApplicationDbContext CreateContext() => new(CreateOptions(ConnectionString));

    public IDbContextFactory<ApplicationDbContext> CreateFactory() => new TestDbFactory(ConnectionString);

    private static DbContextOptions<ApplicationDbContext> CreateOptions(string connectionString)
    {
        var services = new ServiceCollection();
        services.Configure<IdentityOptions>(identity =>
            identity.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        var applicationServices = services.BuildServiceProvider();
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .UseApplicationServiceProvider(applicationServices)
            .Options;
    }

    private sealed class TestDbFactory(string connectionString) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(CreateOptions(connectionString));
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlTestGroup : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}
