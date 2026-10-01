using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Configuration;

public sealed class PaymentModeConfiguration : IEntityTypeConfiguration<PaymentMode>
{
    public void Configure(EntityTypeBuilder<PaymentMode> builder)
    {
        builder.ToTable("PaymentModes", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.RequiresChequeDetails).IsRequired();

        // Phase 60. Other backfills every existing mode (see PaymentMode.Kind); ValueGeneratedNever
        // because Other is not default(PaymentModeKind) -- the phase-2 bug #2 shape exactly.
        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PaymentModeKind.Other)
            .ValueGeneratedNever();

        builder.HasOne<Domain.Accounting.Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
    }
}
