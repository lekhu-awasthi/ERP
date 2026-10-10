using ErpApp.Domain.Configuration;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Configuration;

public sealed class PrintingTemplateLocationChoiceConfiguration : IEntityTypeConfiguration<PrintingTemplateLocationChoice>
{
    public void Configure(EntityTypeBuilder<PrintingTemplateLocationChoice> builder)
    {
        builder.ToTable("PrintingTemplateLocationChoices", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.LocationId).IsRequired();
        builder.Property(x => x.PrintingTemplateId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // One choice per type per location; also the tenant index the convention asks for, and the print handler's
        // lookup (organization, type, location).
        builder.HasIndex(x => new { x.OrganizationId, x.DocumentType, x.LocationId }).IsUnique();

        // Deleting a template takes its location choices with it, so those locations print with the default again.
        builder.HasOne<PrintingTemplate>().WithMany().HasForeignKey(x => x.PrintingTemplateId).OnDelete(DeleteBehavior.Cascade);

        // A location is never deleted (only deactivated), and a second cascade path would be refused by SQL Server.
        builder.HasOne<BillingLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
    }
}
