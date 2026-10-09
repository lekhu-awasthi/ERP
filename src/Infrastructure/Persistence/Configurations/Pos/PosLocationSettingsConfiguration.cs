using ErpApp.Domain.Accounting;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Pos;

public sealed class PosLocationSettingsConfiguration : IEntityTypeConfiguration<PosLocationSettings>
{
    public void Configure(EntityTypeBuilder<PosLocationSettings> builder)
    {
        builder.ToTable("PosLocationSettings", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.ServiceChargeRate).HasPrecision(5, 2).IsRequired();

        // Phase 70 -- None and zero on every existing row: no location charged a packaging fee before.
        builder.Property(x => x.PackagingFeeBasis)
            .HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(PackagingFeeBasis.None).ValueGeneratedNever();
        builder.Property(x => x.PackagingFeeValue).HasPrecision(9, 2).IsRequired().HasDefaultValue(0m).ValueGeneratedNever();
        builder.Ignore(x => x.PackagingFee);
        builder.Property(x => x.DefaultTab).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CreatedAt).IsRequired();

        // A short list of note values, always read and written whole, never queried into: one
        // delimited column ("1000,500,100,...") reads plainly in a sqlcmd proof, where a child table
        // would be nine rows per location for nothing. The same shape as
        // CustomFieldDefinition.ApplicableDocumentTypes. The comparer makes an in-place change
        // visible to the change tracker, not only a reassignment.
        builder.Property(x => x.Denominations)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
                new ValueComparer<IReadOnlyList<int>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x)),
                    v => v.ToList()))
            .HasMaxLength(200)
            .IsRequired();

        // One row per location. Leads on OrganizationId, which is what TenantIndexConvention asks of
        // a master-data table.
        builder.HasIndex(x => new { x.OrganizationId, x.BillingLocationId }).IsUnique();

        builder.HasOne<BillingLocation>()
            .WithMany()
            .HasForeignKey(x => x.BillingLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.ServiceChargeAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.RoundOffAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.PackagingFeeAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
