using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class VariantAttributeOptionConfiguration : IEntityTypeConfiguration<VariantAttributeOption>
{
    public void Configure(EntityTypeBuilder<VariantAttributeOption> builder)
    {
        builder.ToTable("VariantAttributeOptions", schema: "catalog");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Value).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasIndex(x => x.VariantAttributeId);
    }
}
