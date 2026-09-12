using ErpApp.Domain.Catalog;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>
/// Phase 36. Same shape as <see cref="ProductSecondaryUnitConfiguration"/>: a child table of
/// Products in the catalog schema, unique on its pair, with a Restrict FK to the other aggregate it
/// references so a billing location carrying product restrictions cannot be deleted out from under
/// them. It carries no <c>OrganizationId</c> of its own -- both its parents have one and neither can
/// belong to another tenant -- so phase-34c's <c>TenantIndexConvention</c> skips it, as it does
/// every other child table here.
/// </summary>
public sealed class ProductLocationConfiguration : IEntityTypeConfiguration<ProductLocation>
{
    public void Configure(EntityTypeBuilder<ProductLocation> builder)
    {
        builder.ToTable("ProductLocations", schema: "catalog");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.ProductId, x.LocationId }).IsUnique();

        builder.HasOne<BillingLocation>()
            .WithMany()
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
