using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

/// <summary>
/// Phase 41 -- the plan catalogue and its three seeded rows.
///
/// <para><b>Seeded through <c>HasData</c>, with no command behind it</b>, because the catalogue is
/// the vendor's and this codebase models one party. Changing the price list is therefore a migration,
/// which is an honest representation of the fact that nobody inside the product may change it -- and
/// a far better failure mode than a screen that lets a tenant invent its own plan.</para>
///
/// <para><b>The figures are the published ones</b>, read from tiggapp.com/pricing on 2026-09-14 and
/// recorded in docs/phase-41-status.md with the tier matrix they came from. They are list prices
/// before VAT ("13% VAT is applicable in all prices unless otherwise specified") and before add-ons;
/// what a given tenant pays and is capped at lives on its own <see cref="TenantSubscription"/>, which
/// is why a negotiated rate needs no new catalogue row.</para>
/// </summary>
public sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    // Fixed ids, in the 0003 block, so the seed is re-appliable and a subscription's PlanId is
    // stable across environments -- the same idiom as the role and permission seeds.
    private static readonly Guid BasicId = Guid.Parse("00000000-0000-0000-0003-000000000001");
    private static readonly Guid StandardId = Guid.Parse("00000000-0000-0000-0003-000000000002");
    private static readonly Guid ProfessionalId = Guid.Parse("00000000-0000-0000-0003-000000000003");

    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("SubscriptionPlans", schema: "tenancy");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(300).IsRequired();
        builder.Property(x => x.DisplayOrder).IsRequired();

        // The same money precision every amount in this schema uses.
        builder.Property(x => x.AnnualAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.ProductQuota).IsRequired();
        builder.Property(x => x.TransactionQuota).IsRequired();
        builder.Property(x => x.TrackInventoryIncluded).IsRequired();
        builder.Property(x => x.MultipleWarehousesIncluded).IsRequired();
        builder.Property(x => x.LandedCostIncluded).IsRequired();
        builder.Property(x => x.ManufacturingIncluded).IsRequired();
        builder.Property(x => x.PosIncluded).IsRequired();
        builder.Property(x => x.MultiCurrencyIncluded).IsRequired();
        builder.Property(x => x.DeveloperApiIncluded).IsRequired();

        // Not filtered: Code is required, so there are no NULLs for SQL Server's
        // NULLs-are-equal rule to collide over.
        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasData(
            Anonymous(
                BasicId, "Basic", "Basic", 1, 15_000m, productQuota: 1_000, transactionQuota: 30_000,
                "Best for service based businesses that require basic accounting",
                trackInventory: false, multipleWarehouses: false, landedCost: false,
                manufacturing: false, pos: false, multiCurrency: true, developerApi: false),
            Anonymous(
                StandardId, "Standard", "Standard", 2, 20_000m, productQuota: 5_000, transactionQuota: 50_000,
                "Best for SME organizations that require accounting & inventory tracking",
                trackInventory: true, multipleWarehouses: true, landedCost: true,
                manufacturing: false, pos: false, multiCurrency: true, developerApi: false),
            Anonymous(
                ProfessionalId, "Professional", "Professional", 3, 32_000m, productQuota: 10_000, transactionQuota: 200_000,
                "Best for Retail/Restaurants that require POS along with accounting & inventory tracking",
                trackInventory: true, multipleWarehouses: true, landedCost: true,
                manufacturing: true, pos: true, multiCurrency: true, developerApi: true));
    }

    /// <summary>
    /// <c>HasData</c> cannot take an entity whose constructor is private and whose setters are
    /// private, so each seed row is an anonymous object naming the shadow-free columns directly --
    /// the same accommodation <c>RolePermissionConfiguration</c> makes. The Domain factory still
    /// guards every runtime construction; this path is compile-time constants reviewed in a
    /// migration, which is the one place that trade is acceptable.
    /// </summary>
    private static object Anonymous(
        Guid id,
        string code,
        string name,
        int displayOrder,
        decimal annualAmount,
        int productQuota,
        int transactionQuota,
        string description,
        bool trackInventory,
        bool multipleWarehouses,
        bool landedCost,
        bool manufacturing,
        bool pos,
        bool multiCurrency,
        bool developerApi)
    {
        return new
        {
            Id = id,
            Code = code,
            Name = name,
            Description = description,
            DisplayOrder = displayOrder,
            AnnualAmount = annualAmount,
            ProductQuota = productQuota,
            TransactionQuota = transactionQuota,
            TrackInventoryIncluded = trackInventory,
            MultipleWarehousesIncluded = multipleWarehouses,
            LandedCostIncluded = landedCost,
            ManufacturingIncluded = manufacturing,
            PosIncluded = pos,
            MultiCurrencyIncluded = multiCurrency,
            DeveloperApiIncluded = developerApi,
        };
    }
}
