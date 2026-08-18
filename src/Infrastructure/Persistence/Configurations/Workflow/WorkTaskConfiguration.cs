using ErpApp.Domain.Configuration;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Workflow;

public sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable("Tasks", schema: "workflow");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        // ParentType/ParentId are a polymorphic (ParentType, ParentId) pair, not a real FK -- see
        // WorkTask's own doc comment -- so no HasOne/HasForeignKey against Contacts/Organizations.
        builder.Property(x => x.ParentType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ParentId).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.DueDate);
        builder.Property(x => x.TaskTypeId).IsRequired();
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsPrivate).IsRequired();
        builder.Property(x => x.CreatedByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.ParentType, x.ParentId, x.Status });

        builder.HasOne<TaskType>().WithMany().HasForeignKey(x => x.TaskTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
