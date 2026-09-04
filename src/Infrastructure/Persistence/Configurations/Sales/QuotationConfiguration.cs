using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Sales;

public sealed class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("Quotations", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        // Phase 27b -- no HasMaxLength: the reference product's terms editor is a rich-text
        // box with no visible cap, and a truncated legal clause is a worse failure than a
        // wide column. nvarchar(max), same call this codebase already makes for Notes.
        builder.Property(x => x.Terms);
        builder.Property(x => x.DiscountPct).HasPrecision(18, 4).IsRequired();

        builder.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);

        // SetNull, not Restrict/Cascade: CustomStatusId carries no GL/financial weight (Phase 20b),
        // so deleting the CustomStatus definition should just clear the assignment, not block the
        // delete or (Cascade's real risk here) delete the Quotation itself.
        builder.HasOne<CustomStatus>().WithMany().HasForeignKey(x => x.CustomStatusId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("QuotationId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Quotation.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
