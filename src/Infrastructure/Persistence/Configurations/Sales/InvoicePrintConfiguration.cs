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

        // Phase 67: every row before it was a till receipt, so the default is true of them all and the
        // NOT NULL column needs no backfill. ValueGeneratedNever, or EF would send nothing for the
        // CLR default and let the database decide (the known enum-default gotcha).
        builder.Property(x => x.Medium)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PrintMedium.TillReceipt)
            .ValueGeneratedNever();

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
