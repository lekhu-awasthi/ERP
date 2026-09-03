using ErpApp.Domain.Contacts;
using ErpApp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Contacts;

public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments", schema: "contacts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();

        // Phase 27a: ParentType/ParentId replace the Phase 18 ContactId FK. A polymorphic pair
        // cannot carry a real foreign key -- it points at whichever table ParentType names -- so the
        // ON DELETE CASCADE from Contacts is gone with it, matching how WorkTask and Attachment have
        // always been configured. Deleting a Contact is already a deactivate, not a row delete
        // (DeactivateContactCommand), so nothing relied on that cascade.
        builder.Property(x => x.ParentType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.ParentId).IsRequired();

        builder.Property(x => x.Content).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.AuthorUserId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.ParentType, x.ParentId });

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
