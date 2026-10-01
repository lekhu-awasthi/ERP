using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Pos;

public sealed class PosLocationPaymentModeConfiguration : IEntityTypeConfiguration<PosLocationPaymentMode>
{
    public void Configure(EntityTypeBuilder<PosLocationPaymentMode> builder)
    {
        builder.ToTable("PosLocationPaymentModes", schema: "pos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.BillingLocationId, x.PaymentModeId }).IsUnique();

        builder.HasOne<BillingLocation>()
            .WithMany()
            .HasForeignKey(x => x.BillingLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deleting a payment mode deletes its links: the mode's own delete is the decision, and a
        // link to a missing mode would be a till tab that cannot post. See PosLocationPaymentMode.
        builder.HasOne<PaymentMode>()
            .WithMany()
            .HasForeignKey(x => x.PaymentModeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
