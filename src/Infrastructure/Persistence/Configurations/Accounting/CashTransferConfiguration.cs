using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class CashTransferConfiguration : IEntityTypeConfiguration<CashTransfer>
{
    public void Configure(EntityTypeBuilder<CashTransfer> builder)
    {
        builder.ToTable("CashTransfers", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.FromAccountId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("CashTransferId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(CashTransfer.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
