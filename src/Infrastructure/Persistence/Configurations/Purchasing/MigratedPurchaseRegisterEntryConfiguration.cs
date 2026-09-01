using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Purchasing;

public sealed class MigratedPurchaseRegisterEntryConfiguration
    : IEntityTypeConfiguration<MigratedPurchaseRegisterEntry>
{
    public void Configure(EntityTypeBuilder<MigratedPurchaseRegisterEntry> builder)
    {
        builder.ToTable("MigratedPurchaseRegisterEntries", schema: "purchasing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.DocumentCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ImportDeclarationNo).HasMaxLength(50);
        builder.Property(x => x.PartyName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PartyPan).HasMaxLength(20);
        builder.Property(x => x.CreatedAt).IsRequired();

        foreach (var money in new[]
                 {
                     nameof(MigratedPurchaseRegisterEntry.TaxExemptValue),
                     nameof(MigratedPurchaseRegisterEntry.TaxableNonCapitalLocalValue),
                     nameof(MigratedPurchaseRegisterEntry.TaxableNonCapitalLocalVat),
                     nameof(MigratedPurchaseRegisterEntry.TaxableNonCapitalImportValue),
                     nameof(MigratedPurchaseRegisterEntry.TaxableNonCapitalImportVat),
                     nameof(MigratedPurchaseRegisterEntry.TaxableCapitalValue),
                     nameof(MigratedPurchaseRegisterEntry.TaxableCapitalVat),
                 })
        {
            builder.Property<decimal>(money).HasPrecision(18, 2).IsRequired();
        }

        // Re-import safety as a database constraint -- see the Sales-side configuration for the
        // full reasoning, including the InMemory caveat.
        builder.HasIndex(x => new { x.OrganizationId, x.DocumentCode }).IsUnique();

        builder.HasIndex(x => new { x.OrganizationId, x.Date });

        // No FK to Contacts, for the same reason as the Sales side.
        builder.Property(x => x.ContactId);
    }
}
