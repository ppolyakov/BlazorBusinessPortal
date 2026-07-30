using System.Security.Claims;
using BusinessPortal.Application;
using BusinessPortal.Infrastructure;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.Web.Services;

public sealed class CurrentUser(
    AuthenticationStateProvider authenticationStateProvider,
    IDbContextFactory<ApplicationDbContext> dbFactory) : ICurrentUser
{
    public async Task<CurrentUserInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new ForbiddenException("Sign in is required.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var identity = await (
            from user in db.Users.AsNoTracking()
            join organization in db.Organizations.AsNoTracking() on user.OrganizationId equals organization.Id
            where user.Id == userId && user.IsActive && organization.IsActive
            select new { user.Id, user.OrganizationId, OrganizationName = organization.Name, user.DisplayName })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException("This account or organization is inactive.");
        var roles = principal.FindAll(ClaimTypes.Role).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        return new(identity.Id, identity.OrganizationId, identity.OrganizationName, identity.DisplayName, roles);
    }
}
