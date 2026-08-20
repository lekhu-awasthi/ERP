using ErpApp.Domain.Identity;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Workflow;

public sealed class AuditConfiguration : IEntityTypeConfiguration<Audit>
{
    public void Configure(EntityTypeBuilder<Audit> builder)
    {
        builder.ToTable("Audits", schema: "workflow");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.DocumentId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Backs both this phase's System Audit report filters and the future DocumentId-only
        // Activity-tab lookup (Audit's own doc comment) -- OrganizationId first in both so every
        // query stays tenant-scoped.
        builder.HasIndex(x => new { x.OrganizationId, x.DocumentType, x.DocumentId });
        builder.HasIndex(x => new { x.OrganizationId, x.UserId, x.CreatedAt });
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
