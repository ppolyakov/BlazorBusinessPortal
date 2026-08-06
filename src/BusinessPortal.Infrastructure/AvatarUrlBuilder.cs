namespace BusinessPortal.Infrastructure;

internal static class AvatarUrlBuilder
{
    public static string? For(string userId, bool hasAvatar) =>
        hasAvatar ? $"/avatars/{Uri.EscapeDataString(userId)}" : null;
}
