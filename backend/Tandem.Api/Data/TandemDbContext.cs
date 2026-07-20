using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tandem.Api.Data.Entities;

namespace Tandem.Api.Data;

public class TandemDbContext : IdentityDbContext<ApplicationUser>
{
    public TandemDbContext(DbContextOptions<TandemDbContext> options) : base(options) { }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentPermission> DocumentPermissions => Set<DocumentPermission>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ApplicationUser
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(u => u.AvatarColor).HasMaxLength(10).HasDefaultValue("#6366F1");
        });

        // Document
        builder.Entity<Document>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Title).HasMaxLength(500).IsRequired();
            entity.Property(d => d.YjsState).HasColumnType("bytea");
            entity.Property(d => d.IsDeleted).HasDefaultValue(false);

            entity.HasOne(d => d.Owner)
                .WithMany(u => u.OwnedDocuments)
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(d => d.OwnerId);
            entity.HasQueryFilter(d => !d.IsDeleted);
        });

        // DocumentVersion
        builder.Entity<DocumentVersion>(entity =>
        {
            entity.HasKey(v => v.Id);
            entity.Property(v => v.YjsSnapshot).HasColumnType("bytea").IsRequired();
            entity.Property(v => v.Label).HasMaxLength(200);

            entity.HasOne(v => v.Document)
                .WithMany(d => d.Versions)
                .HasForeignKey(v => v.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(v => v.CreatedBy)
                .WithMany(u => u.CreatedVersions)
                .HasForeignKey(v => v.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(v => new { v.DocumentId, v.VersionNumber }).IsUnique();
        });

        // DocumentPermission
        builder.Entity<DocumentPermission>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Role).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(p => p.Document)
                .WithMany(d => d.Permissions)
                .HasForeignKey(p => p.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.User)
                .WithMany(u => u.DocumentPermissions)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(p => new { p.DocumentId, p.UserId }).IsUnique();
        });
    }
}
