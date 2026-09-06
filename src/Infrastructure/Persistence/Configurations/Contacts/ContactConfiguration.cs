using ErpApp.Domain.Contacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Contacts;

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("Contacts", schema: "contacts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        // No HasDefaultValue -- Type is always set explicitly by Contact.Create, so the
        // enum-default EF gotcha (CLAUDE.md's known gotchas) never applies here.
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(300);
        builder.Property(x => x.Pan).HasMaxLength(30);
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.OpeningBalance).HasPrecision(18, 4).IsRequired();

        // Phase 31 (credit control). CreditLimit is non-nullable with a 0 default so the ADD COLUMN
        // backfills every existing row as "no limit" -- see Contact.CreditLimit for why 0 carries
        // that meaning rather than being a limit of zero. No .ValueGeneratedNever() needed: these
        // are not enums, so the default-sentinel gotcha that TenantSettingsConfiguration documents
        // does not apply.
        builder.Property(x => x.CreditLimit).HasPrecision(18, 4).IsRequired().HasDefaultValue(0m);
        builder.Property(x => x.AcceptsReverseTransactions).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();

        builder.HasOne<ContactGroup>()
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Configuration.CreditTerm>()
            .WithMany()
            .HasForeignKey(x => x.CreditTermId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
