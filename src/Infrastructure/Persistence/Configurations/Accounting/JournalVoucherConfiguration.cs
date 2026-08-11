using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class JournalVoucherConfiguration : IEntityTypeConfiguration<JournalVoucher>
{
    public void Configure(EntityTypeBuilder<JournalVoucher> builder)
    {
        builder.ToTable("JournalVouchers", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        // No HasDefaultValue -- Status is always set explicitly by JournalVoucher.Create.
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        // Encapsulated child collection (private backing field), same mapping shape as
        // Catalog.Product.SecondaryUnits -- Cascade since JournalVoucherLine is owned by this
        // aggregate, not a reference to another one.
        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("JournalVoucherId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(JournalVoucher.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
