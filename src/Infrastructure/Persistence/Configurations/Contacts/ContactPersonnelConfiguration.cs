using ErpApp.Domain.Contacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Contacts;

public sealed class ContactPersonnelConfiguration : IEntityTypeConfiguration<ContactPersonnel>
{
    public void Configure(EntityTypeBuilder<ContactPersonnel> builder)
    {
        builder.ToTable("ContactPersonnel", schema: "contacts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.ContactId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(300);
        builder.Property(x => x.Code).HasMaxLength(30);
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.OrganizationTitle).HasMaxLength(100);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.ContactId });

        builder.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ContactGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
    }
}
