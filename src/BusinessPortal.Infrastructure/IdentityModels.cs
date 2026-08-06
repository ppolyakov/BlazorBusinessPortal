using BusinessPortal.Domain;
using Microsoft.AspNetCore.Identity;

namespace BusinessPortal.Infrastructure;

public sealed class ApplicationUser : IdentityUser
{
    public Guid OrganizationId { get; set; }
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public byte[]? AvatarImage { get; set; }
    public string? AvatarContentType { get; set; }
    public DateTime? AvatarUpdatedAtUtc { get; set; }
    public Organization Organization { get; set; } = null!;
}
