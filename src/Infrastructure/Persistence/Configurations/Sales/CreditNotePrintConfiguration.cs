using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Sales;

/// <summary>Phase 63 -- the sibling of <see cref="InvoicePrintConfiguration"/>: the unique
/// (organization, credit note, number) index is what makes the print number a count.</summary>
public sealed class CreditNotePrintConfiguration : IEntityTypeConfiguration<CreditNotePrint>
{
    public void Configure(EntityTypeBuilder<CreditNotePrint> builder)
    {
        builder.ToTable("CreditNotePrints", schema: "sales");

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

        builder.HasIndex(x => new { x.OrganizationId, x.CreditNoteId, x.PrintNumber }).IsUnique();

        builder.HasOne<CreditNote>()
            .WithMany()
            .HasForeignKey(x => x.CreditNoteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
