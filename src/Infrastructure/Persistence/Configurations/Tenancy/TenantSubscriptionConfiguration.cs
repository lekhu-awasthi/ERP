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
        builder.Property(s => s.TrialStartsAt).IsRequired();
        builder.Property(s => s.TrialEndsAt).IsRequired();
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
    }
}
