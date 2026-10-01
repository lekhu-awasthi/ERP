using ErpApp.Domain.Accounting;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Pos;

public sealed class PosSessionConfiguration : IEntityTypeConfiguration<PosSession>
{
    // A count is replaced whole and never edited in place, so comparing the stored text is exactly
    // "did the count change".
    private static readonly ValueComparer<CashCount?> CashCountComparer = new(
        (a, b) => (a == null ? null : a.Serialize()) == (b == null ? null : b.Serialize()),
        v => v == null ? 0 : v.Serialize().GetHashCode(StringComparison.Ordinal),
        v => v == null ? null : CashCount.Parse(v.Serialize()));

    public void Configure(EntityTypeBuilder<PosSession> builder)
    {
        builder.ToTable("PosSessions", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.OpenedAt).IsRequired();
        builder.Property(x => x.OpeningFloat).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ExpectedCash).HasPrecision(18, 4);
        builder.Property(x => x.CountedCash).HasPrecision(18, 4);
        builder.Property(x => x.CashDifference).HasPrecision(18, 4);
        builder.Property(x => x.ClosingNote).HasMaxLength(PosSession.MaxNoteLength);
        builder.Property(x => x.LastActivityAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Property(x => x.OpeningCount)
            .HasConversion(v => v!.Serialize(), v => CashCount.Parse(v), CashCountComparer)
            .HasMaxLength(400);
        builder.Property(x => x.ClosingCount)
            .HasConversion(v => v!.Serialize(), v => CashCount.Parse(v), CashCountComparer)
            .HasMaxLength(400);

        // One open session per cashier per till, as a constraint rather than a read-then-write check:
        // two tabs opening at once must not both win. Filtered, so a cashier's closed history does not
        // collide with the session they open today. It also leads with OrganizationId, which is what
        // TenantIndexConvention asks of a tenant table that has no business date of its own.
        builder.HasIndex(x => new { x.OrganizationId, x.BillingLocationId, x.UserId })
            .IsUnique()
            .HasFilter("[Status] = 'Open'");

        // The day report and the session list read a location's sessions newest first.
        builder.HasIndex(x => new { x.OrganizationId, x.BillingLocationId, x.OpenedAt })
            .IsDescending(false, false, true);

        builder.HasOne<BillingLocation>()
            .WithMany()
            .HasForeignKey(x => x.BillingLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.CashAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.CashMovements)
            .WithOne()
            .HasForeignKey(x => x.PosSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PosSession.CashMovements))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class PosCashMovementConfiguration : IEntityTypeConfiguration<PosCashMovement>
{
    public void Configure(EntityTypeBuilder<PosCashMovement> builder)
    {
        builder.ToTable("PosCashMovements", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(PosSession.MaxNoteLength);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Ignore(x => x.SignedAmount);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
