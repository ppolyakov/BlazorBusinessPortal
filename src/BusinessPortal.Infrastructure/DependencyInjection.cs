using BusinessPortal.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessPortal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is required.");
        services.AddSingleton<DemoIdentityMutationInterceptor>();
        services.AddDbContextFactory<ApplicationDbContext>((provider, options) =>
            options
                .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3))
                .AddInterceptors(provider.GetRequiredService<DemoIdentityMutationInterceptor>()));
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IWorkItemService, WorkItemService>();
        services.AddScoped<ITimeEntryService, TimeEntryService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<DemoDataSeeder>();
        services.AddSingleton(TimeProvider.System);
        services.AddHostedService<DemoResetHostedService>();
        return services;
    }
}
