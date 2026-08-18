using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Crm;
using ErpApp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Crm;

public sealed class DealConfiguration : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> builder)
    {
        builder.ToTable("Deals", schema: "crm");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.ContactId).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.ExpectedRevenue).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ExpectedClosingDate);
        builder.Property(x => x.StageId);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsPrivate).IsRequired();
        builder.Property(x => x.ClosingDate);
        builder.Property(x => x.CreatedByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.Status });

        builder.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeadSource>().WithMany().HasForeignKey(x => x.LeadSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DealStage>().WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Encapsulated child collection (private backing field), same pattern as
        // Product.SecondaryUnits -- Cascade here specifically: deleting a Deal should delete its
        // own assignee rows.
        builder.HasMany(x => x.Assignees)
            .WithOne()
            .HasForeignKey(x => x.DealId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Deal.Assignees))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
