using ErpApp.Domain.Identity;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Workflow;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments", schema: "workflow");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        // ParentType/ParentId are a polymorphic pair, not a real FK -- see Attachment's own doc
        // comment, mirroring WorkTaskConfiguration.
        builder.Property(x => x.ParentType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.ParentId).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(260).IsRequired();
        builder.Property(x => x.UploadedByUserId).IsRequired();
        builder.Property(x => x.UploadedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.ParentType, x.ParentId });

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
