using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

public sealed class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> builder)
    {
        builder.ToTable("OrganizationMemberships", schema: "tenancy");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.OrganizationId).IsRequired();
        builder.Property(m => m.InvitedEmail).HasMaxLength(256);
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();

        builder.HasIndex(m => new { m.OrganizationId, m.UserId });
        builder.HasIndex(m => new { m.OrganizationId, m.InvitedEmail });

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        // UserId is nullable (an invite may be addressed to an email with no account yet, see
        // OrganizationMembership's doc comment) so this is a restrict, not a cascade, delete --
        // avoids the multiple-cascade-paths error EF Core/SQL Server would otherwise raise from
        // Organization also cascading into this table.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
