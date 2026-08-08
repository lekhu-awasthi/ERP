using ErpApp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Identity;

public sealed class VerificationCodeConfiguration : IEntityTypeConfiguration<VerificationCode>
{
    public void Configure(EntityTypeBuilder<VerificationCode> builder)
    {
        builder.ToTable("VerificationCodes", schema: "identity");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).HasMaxLength(6).IsRequired();
        builder.Property(c => c.Purpose).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.ExpiresAt).IsRequired();

        builder.HasIndex(c => new { c.UserId, c.Purpose, c.CreatedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
