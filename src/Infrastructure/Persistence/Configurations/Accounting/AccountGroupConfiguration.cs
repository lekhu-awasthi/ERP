using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class AccountGroupConfiguration : IEntityTypeConfiguration<AccountGroup>
{
    public void Configure(EntityTypeBuilder<AccountGroup> builder)
    {
        builder.ToTable("AccountGroups", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        // No HasDefaultValue -- RootType is always set explicitly by AccountGroup.Create, so the
        // enum-default EF gotcha (CLAUDE.md's known gotchas) never applies here.
        builder.Property(x => x.RootType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();

        builder.HasOne<AccountGroup>().WithMany().HasForeignKey(x => x.ParentGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}
