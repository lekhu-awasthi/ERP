using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

public sealed class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> builder)
    {
        builder.ToTable("TenantSubscriptions", schema: "tenancy");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.OrganizationId).IsRequired();
        builder.Property(s => s.PlanName).HasMaxLength(50).IsRequired();
        builder.Property(s => s.OriginatedAt).IsRequired();
        builder.Property(s => s.TermStartsAt).IsRequired();
        builder.Property(s => s.TermEndsAt).IsRequired();
        builder.Property(s => s.SubscriptionAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.ProductQuota).IsRequired();
        builder.Property(s => s.TransactionQuota).IsRequired();
        builder.Property(s => s.DailyAiScanQuota).IsRequired();
        builder.Property(s => s.LocationQuota).IsRequired();
        builder.Property(s => s.IrdVerified).IsRequired();
        builder.Property(s => s.TrackInventoryEnabled).IsRequired();
        builder.Property(s => s.MultipleLocationsEnabled).IsRequired();
        builder.Property(s => s.MultipleWarehousesEnabled).IsRequired();
        builder.Property(s => s.MultiCurrencyEnabled).IsRequired();
        builder.Property(s => s.ManufacturingEnabled).IsRequired();
        builder.Property(s => s.PosRetailEnabled).IsRequired();
        builder.Property(s => s.PosRestaurantEnabled).IsRequired();
        builder.Property(s => s.IrdSyncEnabled).IsRequired();

        builder.HasIndex(s => s.OrganizationId).IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Phase 41. Restrict, not Cascade: a catalogue row is seeded reference data, and deleting a
        // plan that tenants are on must fail loudly rather than quietly unsubscribing them. Nothing
        // deletes one today -- there is no command -- which is exactly why the constraint is the
        // place to say so.
        builder.HasOne<SubscriptionPlan>()
            .WithMany()
            .HasForeignKey(s => s.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
