using BusinessPortal.Domain;
using Microsoft.AspNetCore.Identity;

namespace BusinessPortal.Infrastructure;

public sealed class ApplicationUser : IdentityUser
{
    public Guid OrganizationId { get; set; }
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Organization Organization { get; set; } = null!;
}
