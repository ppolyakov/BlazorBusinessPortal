using BusinessPortal.Application;
using BusinessPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.Infrastructure;

internal static class NotificationWriter
{
    public static async Task ToRolesAsync(
        ApplicationDbContext db,
        Guid organizationId,
        IReadOnlyCollection<string> roleNames,
        string? excludingUserId,
        NotificationType type,
        string title,
        string message,
        string targetUrl,
        string? entityType,
        object? entityId,
        CancellationToken cancellationToken)
    {
        var roles = roleNames.ToArray();
        var recipients = await (
            from recipient in db.Users.AsNoTracking()
            join userRole in db.UserRoles.AsNoTracking() on recipient.Id equals userRole.UserId
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where recipient.OrganizationId == organizationId
                  && recipient.IsActive
                  && recipient.Id != excludingUserId
                  && role.Name != null
                  && roles.Contains(role.Name)
            select recipient.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        AddForRecipients(db, organizationId, recipients, excludingUserId, type, title, message, targetUrl, entityType, entityId);
    }

    public static async Task ToOrganizationAsync(
        ApplicationDbContext db,
        Guid organizationId,
        string? excludingUserId,
        NotificationType type,
        string title,
        string message,
        string targetUrl,
        string? entityType,
        object? entityId,
        CancellationToken cancellationToken)
    {
        var recipients = await db.Users.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IsActive && x.Id != excludingUserId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        AddForRecipients(db, organizationId, recipients, excludingUserId, type, title, message, targetUrl, entityType, entityId);
    }

    public static void ToUser(
        ApplicationDbContext db,
        Guid organizationId,
        string recipientUserId,
        string? actorUserId,
        NotificationType type,
        string title,
        string message,
        string targetUrl,
        string? entityType,
        object? entityId) =>
        AddForRecipients(db, organizationId, [recipientUserId], actorUserId, type, title, message, targetUrl, entityType, entityId);

    private static void AddForRecipients(
        ApplicationDbContext db,
        Guid organizationId,
        IEnumerable<string> recipientUserIds,
        string? actorUserId,
        NotificationType type,
        string title,
        string message,
        string targetUrl,
        string? entityType,
        object? entityId)
    {
        var safeTarget = IsLocalTarget(targetUrl) ? targetUrl : "/";
        var nowUtc = DateTime.UtcNow;
        foreach (var recipientUserId in recipientUserIds.Distinct(StringComparer.Ordinal))
        {
            db.Notifications.Add(new Notification
            {
                OrganizationId = organizationId,
                RecipientUserId = recipientUserId,
                ActorUserId = actorUserId,
                Type = type,
                Title = Truncate(title, 160),
                Message = Truncate(message, 500),
                TargetUrl = safeTarget,
                EntityType = string.IsNullOrWhiteSpace(entityType) ? null : Truncate(entityType, 80),
                EntityId = entityId is null ? null : Truncate(entityId.ToString() ?? "", 80),
                CreatedAtUtc = nowUtc
            });
        }
    }

    private static bool IsLocalTarget(string targetUrl) =>
        targetUrl.StartsWith('/') && !targetUrl.StartsWith("//", StringComparison.Ordinal);

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}

internal sealed class NotificationService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser)
    : PortalService(factory, currentUser), INotificationService
{
    public async Task<NotificationFeed> GetAsync(int take = 10, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var query = db.Notifications.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.RecipientUserId == user.UserId);
        var unreadCount = await query.CountAsync(x => x.ReadAtUtc == null, cancellationToken);
        var rows = await (from notification in query
                          join actor in db.Users.AsNoTracking() on notification.ActorUserId equals actor.Id into actors
                          from actor in actors.DefaultIfEmpty()
                          orderby notification.CreatedAtUtc descending
                          select new
                          {
                              notification.Id,
                              notification.Type,
                              notification.Title,
                              notification.Message,
                              notification.TargetUrl,
                              notification.CreatedAtUtc,
                              notification.ReadAtUtc,
                              ActorUserId = actor == null ? null : actor.Id,
                              ActorName = actor == null ? null : actor.DisplayName,
                              ActorHasAvatar = actor != null && actor.AvatarImage != null
                          })
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(cancellationToken);
        var items = rows.Select(x => new NotificationListItem(
                x.Id,
                x.Type,
                x.Title,
                x.Message,
                x.TargetUrl,
                x.CreatedAtUtc,
                x.ReadAtUtc,
                x.ActorName,
                x.ActorUserId is null ? null : AvatarUrlBuilder.For(x.ActorUserId, x.ActorHasAvatar)))
            .ToList();
        return new(items, unreadCount);
    }

    public async Task<string> MarkReadAsync(long id, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var notification = await db.Notifications.SingleOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == user.OrganizationId && x.RecipientUserId == user.UserId,
            cancellationToken)
            ?? throw new ResourceNotFoundException("Notification was not found.");
        if (notification.ReadAtUtc is null)
        {
            notification.ReadAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return notification.TargetUrl;
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var nowUtc = DateTime.UtcNow;
        await db.Notifications
            .Where(x => x.OrganizationId == user.OrganizationId && x.RecipientUserId == user.UserId && x.ReadAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReadAtUtc, nowUtc), cancellationToken);
    }
}
