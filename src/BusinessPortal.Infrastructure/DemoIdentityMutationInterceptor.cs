using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace BusinessPortal.Infrastructure;

public sealed class DemoIdentityMutationInterceptor(
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        EnsureAccountChangesAllowed(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnsureAccountChangesAllowed(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void EnsureAccountChangesAllowed(DbContext? context)
    {
        if (context is null
            || !configuration.GetValue<bool>("SeedDemoData")
            || !configuration.GetValue<bool>("DemoAccess:Enabled")
            || httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var identityChanged = context.ChangeTracker.Entries().Any(entry =>
            (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            && entry.Metadata.GetTableName()?.StartsWith("AspNet", StringComparison.Ordinal) == true);

        if (identityChanged)
        {
            throw new InvalidOperationException("Account changes are disabled in the public demo.");
        }
    }
}
