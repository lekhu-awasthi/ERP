using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

public sealed class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> builder)
    {
        builder.ToTable("TenantSettings", schema: "tenancy");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.OrganizationId).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();

        // Phase 2 additions (architecture-spec.md §4.10) -- HasDefaultValue so the migration's
        // ADD COLUMN backfills Phase 1b's existing rows with no separate data step (see
        // phase-2-status.md's migration-sequencing notes). .ValueGeneratedNever() on each:
        // without it, EF treats a property whose CLR value equals the type's default(TEnum) (its
        // first-declared, "= 0" member) as "unset," silently substituting the SQL DEFAULT even
        // when TenantSettings.CreateDefault()/UpdateSettings() explicitly chose that value (e.g.
        // InventoryTrackingMode=PhysicalMovement is enum value 0, but the DB default is
        // AccountingMovement) -- confirmed live by dotnet ef's own
        // "database-generated default... has no configured sentinel value" warning when this
        // migration was first scaffolded without it.
        builder.Property(s => s.SuggestSellingPriceMode)
            .HasConversion<string>().HasMaxLength(30).IsRequired()
            .HasDefaultValue(Domain.Tenancy.SuggestSellingPriceMode.RecentSellingPrice).ValueGeneratedNever();
        builder.Property(s => s.ProductPriceBasis)
            .HasConversion<string>().HasMaxLength(30).IsRequired()
            .HasDefaultValue(Domain.Tenancy.ProductPriceBasis.ExclusiveOfVat).ValueGeneratedNever();
        builder.Property(s => s.InventoryTrackingMode)
            .HasConversion<string>().HasMaxLength(30).IsRequired()
            .HasDefaultValue(Domain.Tenancy.InventoryTrackingMode.AccountingMovement).ValueGeneratedNever();
        builder.Property(s => s.NegativeCashBalanceAction)
            .HasConversion<string>().HasMaxLength(30).IsRequired()
            .HasDefaultValue(Domain.Tenancy.BalanceAction.Reject).ValueGeneratedNever();
        builder.Property(s => s.NegativeStockBalanceAction)
            .HasConversion<string>().HasMaxLength(30).IsRequired()
            .HasDefaultValue(Domain.Tenancy.BalanceAction.Warn).ValueGeneratedNever();

        builder.HasIndex(s => s.OrganizationId).IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
