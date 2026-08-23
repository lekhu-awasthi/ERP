using ErpApp.Domain.Identity;
using ErpApp.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Crm;

public sealed class SmsCreditLedgerEntryConfiguration : IEntityTypeConfiguration<SmsCreditLedgerEntry>
{
    public void Configure(EntityTypeBuilder<SmsCreditLedgerEntry> builder)
    {
        builder.ToTable("SmsCreditLedgerEntries", schema: "crm");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ChangeAmount).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.CreatedByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.OrganizationId);

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
