using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

public sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies", schema: "tenancy");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Symbol).HasMaxLength(10).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // IsBaseCurrency is derived from Code (see the aggregate) and is deliberately not stored --
        // Ignore it explicitly rather than relying on EF's read-only-property inference, so a later
        // refactor that gives it a setter fails loudly here instead of quietly adding a column.
        builder.Ignore(x => x.IsBaseCurrency);

        // The tenant's currency list is keyed by code, and documents reference that code -- so two
        // rows sharing one is not a duplicate-name annoyance, it is an ambiguity in what every
        // document written afterwards means.
        builder.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();
    }
}
