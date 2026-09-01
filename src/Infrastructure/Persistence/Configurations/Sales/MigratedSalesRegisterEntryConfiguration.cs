using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Sales;

public sealed class MigratedSalesRegisterEntryConfiguration : IEntityTypeConfiguration<MigratedSalesRegisterEntry>
{
    public void Configure(EntityTypeBuilder<MigratedSalesRegisterEntry> builder)
    {
        builder.ToTable("MigratedSalesRegisterEntries", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.DocumentCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PartyName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PartyPan).HasMaxLength(20);
        builder.Property(x => x.ExportCountry).HasMaxLength(100);
        builder.Property(x => x.ExportDeclarationNo).HasMaxLength(50);
        builder.Property(x => x.CreatedAt).IsRequired();

        foreach (var money in new[]
                 {
                     nameof(MigratedSalesRegisterEntry.TotalValue),
                     nameof(MigratedSalesRegisterEntry.TaxExemptValue),
                     nameof(MigratedSalesRegisterEntry.TaxableValue),
                     nameof(MigratedSalesRegisterEntry.VatAmount),
                     nameof(MigratedSalesRegisterEntry.ExportValue),
                 })
        {
            builder.Property<decimal>(money).HasPrecision(18, 2).IsRequired();
        }

        // Re-import safety, as a database constraint rather than a hope (Decision A). A cutover
        // import is the upload most likely to be run twice by accident, and a silent duplicate here
        // means a doubled statutory sales figure -- so the prior system's own document number is the
        // natural key and the database says so. The handler's own pre-check produces the readable
        // per-row message; this index is what holds under two runners racing.
        //
        // Note the InMemory provider enforces no unique index at all, so the race path is
        // unreachable from tests and was verified against real SQL Server instead -- the same
        // caveat that applies to ImportJobRow's (job, row number) index.
        builder.HasIndex(x => new { x.OrganizationId, x.DocumentCode }).IsUnique();

        // The register query: this organization, ordered within a date window.
        builder.HasIndex(x => new { x.OrganizationId, x.Date });

        // No FK to Contacts on purpose. ContactId is a best-effort link to an existing Contact by
        // exact PAN and is null for most migrated rows; a required relationship would force the
        // cutover to import every historical customer first, and a cascade would silently delete
        // filed statutory history when a Contact is removed.
        builder.Property(x => x.ContactId);
    }
}
