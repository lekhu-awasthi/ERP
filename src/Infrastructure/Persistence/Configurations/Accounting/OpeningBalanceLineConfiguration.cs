using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class OpeningBalanceLineConfiguration : IEntityTypeConfiguration<OpeningBalanceLine>
{
    public void Configure(EntityTypeBuilder<OpeningBalanceLine> builder)
    {
        builder.ToTable("OpeningBalanceLines", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Debit).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Credit).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.AccountId }).IsUnique();

        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
