using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Purchasing;

public sealed class DebitNoteConfiguration : IEntityTypeConfiguration<DebitNote>
{
    public void Configure(EntityTypeBuilder<DebitNote> builder)
    {
        builder.ToTable("DebitNotes", schema: "purchasing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.ReferrerType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ReferrerId);

        builder.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("DebitNoteId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(DebitNote.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
