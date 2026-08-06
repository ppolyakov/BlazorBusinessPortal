using BusinessPortal.Application;
using BusinessPortal.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.Web.Services;

public static class AvatarImagePolicy
{
    public const long MaximumFileSize = 2 * 1024 * 1024;

    public static string? DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8
            && content[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            return "image/jpeg";
        if (content.Length >= 12
            && content[..4].SequenceEqual("RIFF"u8)
            && content.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}

public static class AvatarEndpoints
{
    public static IEndpointRouteBuilder MapAvatarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/avatars/{userId}", GetAvatarAsync).RequireAuthorization();
        endpoints.MapPost("/account/avatar", UploadAvatarAsync).RequireAuthorization();
        endpoints.MapPost("/account/avatar/remove", RemoveAvatarAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetAvatarAsync(
        string userId,
        HttpContext context,
        ICurrentUser currentUser,
        IDbContextFactory<ApplicationDbContext> factory,
        CancellationToken cancellationToken)
    {
        var viewer = await currentUser.GetAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var avatar = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId && x.OrganizationId == viewer.OrganizationId && x.IsActive)
            .Select(x => new { x.AvatarImage, x.AvatarContentType, x.AvatarUpdatedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (avatar?.AvatarImage is null || avatar.AvatarContentType is null)
            return Results.NotFound();

        context.Response.Headers.CacheControl = "private, no-store";
        return Results.File(
            avatar.AvatarImage,
            avatar.AvatarContentType,
            lastModified: avatar.AvatarUpdatedAtUtc is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(avatar.AvatarUpdatedAtUtc.Value, DateTimeKind.Utc)));
    }

    private static async Task<IResult> UploadAvatarAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("avatar");
            if (file is null || file.Length == 0)
                return ProfileRedirect("missing");
            if (file.Length > AvatarImagePolicy.MaximumFileSize)
                return ProfileRedirect("too-large");

            await using var stream = new MemoryStream((int)file.Length);
            await file.CopyToAsync(stream, cancellationToken);
            var content = stream.ToArray();
            var contentType = AvatarImagePolicy.DetectContentType(content);
            if (contentType is null)
                return ProfileRedirect("invalid-type");

            var user = await userManager.GetUserAsync(context.User);
            if (user is null)
                return Results.Unauthorized();
            user.AvatarImage = content;
            user.AvatarContentType = contentType;
            user.AvatarUpdatedAtUtc = DateTime.UtcNow;
            var result = await userManager.UpdateAsync(user);
            return result.Succeeded ? ProfileRedirect("updated") : ProfileRedirect("failed");
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }
        catch (InvalidDataException)
        {
            return ProfileRedirect("too-large");
        }
    }

    private static async Task<IResult> RemoveAvatarAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<ApplicationUser> userManager)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = await userManager.GetUserAsync(context.User);
            if (user is null)
                return Results.Unauthorized();
            user.AvatarImage = null;
            user.AvatarContentType = null;
            user.AvatarUpdatedAtUtc = DateTime.UtcNow;
            var result = await userManager.UpdateAsync(user);
            return result.Succeeded ? ProfileRedirect("removed") : ProfileRedirect("failed");
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }
    }

    private static IResult ProfileRedirect(string status) =>
        Results.LocalRedirect($"/Account/Manage?avatarStatus={Uri.EscapeDataString(status)}");
}
