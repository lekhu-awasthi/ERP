using ErpApp.Domain.Catalog;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Pos;

// Phase 64 -- the restaurant: the floor (areas and tables), kitchen stations, and the open order with
// its lines and kitchen tickets. Everything lives in the `pos` schema beside phase 61's sessions.

public sealed class PosAreaConfiguration : IEntityTypeConfiguration<PosArea>
{
    public void Configure(EntityTypeBuilder<PosArea> builder)
    {
        builder.ToTable("PosAreas", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(PosArea.MaxNameLength).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Two areas of one restaurant called "Rooftop" would be one tab the waiter cannot tell apart.
        builder.HasIndex(x => new { x.OrganizationId, x.BillingLocationId, x.Name }).IsUnique();

        builder.HasOne<BillingLocation>()
            .WithMany()
            .HasForeignKey(x => x.BillingLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Tables)
            .WithOne()
            .HasForeignKey(x => x.PosAreaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PosArea.Tables))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class PosTableConfiguration : IEntityTypeConfiguration<PosTable>
{
    public void Configure(EntityTypeBuilder<PosTable> builder)
    {
        builder.ToTable("PosTables", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(PosArea.MaxNameLength).IsRequired();
        builder.Property(x => x.Shape).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // A table's name is unique at its location, not just its area (PosTable's remarks): the
        // constraint behind the handler's friendlier check, so two editors saving at once cannot both win.
        builder.HasIndex(x => new { x.OrganizationId, x.BillingLocationId, x.Name }).IsUnique();

        builder.HasOne<BillingLocation>()
            .WithMany()
            .HasForeignKey(x => x.BillingLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class KitchenStationConfiguration : IEntityTypeConfiguration<KitchenStation>
{
    public void Configure(EntityTypeBuilder<KitchenStation> builder)
    {
        builder.ToTable("KitchenStations", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(KitchenStation.MaxNameLength).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
    }
}

public sealed class PosOrderConfiguration : IEntityTypeConfiguration<PosOrder>
{
    public void Configure(EntityTypeBuilder<PosOrder> builder)
    {
        builder.ToTable("PosOrders", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.OrderType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.VoidReason).HasMaxLength(PosOrder.MaxReasonLength);
        builder.Property(x => x.LastActivityAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        // One open order per table, as a constraint rather than a read-then-write check: two waiters
        // seating one table at once must not both win. Filtered on both halves explicitly -- EF would
        // add the IS NOT NULL on its own, but the Status half is the rule and should read as one.
        builder.HasIndex(x => new { x.OrganizationId, x.PosTableId })
            .IsUnique()
            .HasFilter("[Status] = 'Open' AND [PosTableId] IS NOT NULL");

        // The floor and the Take Away / Delivery lists read a location's open orders.
        builder.HasIndex(x => new { x.OrganizationId, x.BillingLocationId, x.Status });

        builder.HasOne<BillingLocation>()
            .WithMany()
            .HasForeignKey(x => x.BillingLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PosTable>()
            .WithMany()
            .HasForeignKey(x => x.PosTableId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Contact>()
            .WithMany()
            .HasForeignKey(x => x.ContactId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.PosOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Tickets)
            .WithOne()
            .HasForeignKey(x => x.PosOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PosOrder.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(PosOrder.Tickets))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class PosOrderLineConfiguration : IEntityTypeConfiguration<PosOrderLine>
{
    public void Configure(EntityTypeBuilder<PosOrderLine> builder)
    {
        builder.ToTable("PosOrderLines", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Rate).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ConversionFactor).HasPrecision(18, 6).IsRequired();
        builder.Property(x => x.VatRate).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.ServiceChargeRate).HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(PosOrder.MaxNoteLength);
        builder.Property(x => x.ServedQuantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.IsTakeAway).IsRequired();

        builder.HasIndex(x => new { x.PosOrderId, x.LineNo }).IsUnique();

        // Phase 68 -- a take-away line points at the dine-in line it was parcelled from, on the same order.
        // Restrict: the order already cascades to every line, and a second path is one SQL Server refuses.
        builder.HasOne<PosOrderLine>()
            .WithMany()
            .HasForeignKey(x => x.ParcelledFromLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasurement>()
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<KitchenStation>()
            .WithMany()
            .HasForeignKey(x => x.KitchenStationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class KitchenTicketConfiguration : IEntityTypeConfiguration<KitchenTicket>
{
    public void Configure(EntityTypeBuilder<KitchenTicket> builder)
    {
        builder.ToTable("KitchenTickets", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(PosOrder.MaxReasonLength);
        builder.Property(x => x.PrintCount).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(x => x.IsCancellation);

        // Phase 68 -- a transfer ticket names the other order. Restrict, so neither order can be deleted
        // out from under the other's history (nothing deletes an order today).
        builder.HasOne<PosOrder>()
            .WithMany()
            .HasForeignKey(x => x.CounterpartOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // One ticket per send per station. Null is the Default station and is a real value here, so
        // the index is deliberately unfiltered: SQL Server treats NULLs as equal in a unique index,
        // which is what makes "one Default ticket per send" enforced, and the IS NOT NULL filter EF
        // would add on its own would destroy exactly that (phase 32's HasFilter(null)).
        builder.HasIndex(x => new { x.PosOrderId, x.SendNumber, x.KitchenStationId })
            .IsUnique()
            .HasFilter(null);

        builder.HasOne<KitchenStation>()
            .WithMany()
            .HasForeignKey(x => x.KitchenStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.KitchenTicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(KitchenTicket.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class KitchenTicketLineConfiguration : IEntityTypeConfiguration<KitchenTicketLine>
{
    public void Configure(EntityTypeBuilder<KitchenTicketLine> builder)
    {
        builder.ToTable("KitchenTicketLines", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();

        // Restrict, not Cascade: the order already cascades to its lines and to its tickets, and a
        // second cascade path from a line to its ticket lines is one SQL Server refuses.
        builder.HasOne<PosOrderLine>()
            .WithMany()
            .HasForeignKey(x => x.PosOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
