using BusinessPortal.Application;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.Infrastructure;

internal sealed class UserDirectory(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser) : IUserDirectory
{
    public async Task<IReadOnlyList<LookupItem<string>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var users = await db.Users.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.IsActive)
            .OrderBy(x => x.DisplayName)
            .Select(x => new { x.Id, x.DisplayName, HasAvatar = x.AvatarImage != null })
            .ToListAsync(cancellationToken);
        return users
            .Select(x => new LookupItem<string>(x.Id, x.DisplayName, AvatarUrlBuilder.For(x.Id, x.HasAvatar)))
            .ToList();
    }

    public async Task<IReadOnlyList<LookupItem<string>>> ListManagersAsync(CancellationToken cancellationToken = default)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var managerRoleIds = await db.Roles.AsNoTracking()
            .Where(role => role.Name == PortalRoles.Administrator || role.Name == PortalRoles.Manager)
            .Select(role => role.Id)
            .ToListAsync(cancellationToken);
        var users = await (from account in db.Users.AsNoTracking()
                           join userRole in db.UserRoles.AsNoTracking() on account.Id equals userRole.UserId
                           where account.OrganizationId == user.OrganizationId
                                 && account.IsActive
                                 && account.Id != user.UserId
                                 && managerRoleIds.Contains(userRole.RoleId)
                           select new { account.Id, account.DisplayName, HasAvatar = account.AvatarImage != null })
            .Distinct()
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        return users.Select(x => new LookupItem<string>(x.Id, x.DisplayName, AvatarUrlBuilder.For(x.Id, x.HasAvatar))).ToList();
    }
}
