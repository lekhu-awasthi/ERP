using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class CashTransferLineConfiguration : IEntityTypeConfiguration<CashTransferLine>
{
    public void Configure(EntityTypeBuilder<CashTransferLine> builder)
    {
        builder.ToTable("CashTransferLines", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();

        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.ToAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
