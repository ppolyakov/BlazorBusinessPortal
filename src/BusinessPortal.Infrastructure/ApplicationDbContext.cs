using BusinessPortal.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.Infrastructure;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<WorkItemActivity> WorkItemActivities => Set<WorkItemActivity>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<TimeEntryActivity> TimeEntryActivities => Set<TimeEntryActivity>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Organization>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Slug).HasMaxLength(80);
            entity.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(120);
            entity.Property(x => x.AvatarContentType).HasMaxLength(40);
            entity.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.IsActive });
        });

        builder.Entity<Client>(entity =>
        {
            entity.Property(x => x.Number).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.ContactName).HasMaxLength(120);
            entity.Property(x => x.ContactEmail).HasMaxLength(200);
            entity.Property(x => x.ContactPhone).HasMaxLength(40);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.OrganizationId, x.Name });
            entity.HasIndex(x => new { x.OrganizationId, x.Status });
            entity.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
        });

        builder.Entity<Project>(entity =>
        {
            entity.Property(x => x.Number).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Code).HasMaxLength(30);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.BudgetHours).HasPrecision(8, 2);
            entity.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.ClientId, x.Status });
        });

        builder.Entity<WorkItem>(entity =>
        {
            entity.Property(x => x.Number).ValueGeneratedNever();
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.EstimatedHours).HasPrecision(8, 2);
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.OrganizationId, x.ProjectId, x.Status });
            entity.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.AssignedToUserId, x.DueDate });
        });

        builder.Entity<WorkItemActivity>(entity =>
        {
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Comment).HasMaxLength(1000);
            entity.HasOne<WorkItem>().WithMany().HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.OrganizationId, x.WorkItemId, x.OccurredAtUtc });
        });

        builder.Entity<TimeEntry>(entity =>
        {
            entity.Property(x => x.Number).ValueGeneratedNever();
            entity.Property(x => x.Hours).HasPrecision(6, 2);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ReviewComment).HasMaxLength(500);
            entity.Property(x => x.Version).IsRowVersion().HasColumnName("xmin");
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkItem>().WithMany().HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SubmittedToUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.UserId, x.WorkDate });
            entity.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.Status, x.SubmittedAtUtc });
            entity.HasIndex(x => new { x.OrganizationId, x.ProjectId, x.WorkDate });
        });

        builder.Entity<TimeEntryActivity>(entity =>
        {
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.TargetLabel).HasMaxLength(120);
            entity.Property(x => x.Comment).HasMaxLength(1000);
            entity.HasOne<TimeEntry>().WithMany().HasForeignKey(x => x.TimeEntryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.OrganizationId, x.TimeEntryId, x.OccurredAtUtc });
        });

        builder.Entity<AuditEntry>(entity =>
        {
            entity.Property(x => x.Action).HasMaxLength(80);
            entity.Property(x => x.EntityType).HasMaxLength(80);
            entity.Property(x => x.EntityId).HasMaxLength(80);
            entity.Property(x => x.Summary).HasMaxLength(300);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.OccurredAtUtc });
            entity.HasIndex(x => new { x.OrganizationId, x.EntityType, x.Action });
        });

        builder.Entity<Notification>(entity =>
        {
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.Title).HasMaxLength(160);
            entity.Property(x => x.Message).HasMaxLength(500);
            entity.Property(x => x.TargetUrl).HasMaxLength(300);
            entity.Property(x => x.EntityType).HasMaxLength(80);
            entity.Property(x => x.EntityId).HasMaxLength(80);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.OrganizationId, x.RecipientUserId, x.ReadAtUtc, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.OrganizationId, x.EntityType, x.EntityId });
        });
    }
}
