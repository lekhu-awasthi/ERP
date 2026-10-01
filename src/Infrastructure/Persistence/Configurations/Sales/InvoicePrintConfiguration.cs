using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Sales;

public sealed class InvoicePrintConfiguration : IEntityTypeConfiguration<InvoicePrint>
{
    public void Configure(EntityTypeBuilder<InvoicePrint> builder)
    {
        builder.ToTable("InvoicePrints", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.PrintNumber).IsRequired();
        builder.Property(x => x.PrintedAt).IsRequired();
        builder.Ignore(x => x.IsCopy);

        // The count's enforcement (phase-62-status.md Decision B): two prints racing for the same
        // number cannot both commit. Leads on OrganizationId, which TenantIndexConvention asks of a
        // tenant-scoped table, and it is the only path anything reads these rows by.
        builder.HasIndex(x => new { x.OrganizationId, x.InvoiceId, x.PrintNumber }).IsUnique();

        // Restrict: a printed bill's history is not deleted with anything.
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
