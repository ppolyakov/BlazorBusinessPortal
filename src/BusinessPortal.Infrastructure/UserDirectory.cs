using BusinessPortal.Application;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.Infrastructure;

internal sealed class UserDirectory(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser) : IUserDirectory
{
    public async Task<IReadOnlyList<LookupItem<string>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.IsActive)
            .OrderBy(x => x.DisplayName)
            .Select(x => new LookupItem<string>(x.Id, x.DisplayName))
            .ToListAsync(cancellationToken);
    }
}
