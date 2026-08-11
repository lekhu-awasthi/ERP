using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class JournalVoucherLineConfiguration : IEntityTypeConfiguration<JournalVoucherLine>
{
    public void Configure(EntityTypeBuilder<JournalVoucherLine> builder)
    {
        builder.ToTable("JournalVoucherLines", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Debit).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Credit).HasPrecision(18, 4).IsRequired();

        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
