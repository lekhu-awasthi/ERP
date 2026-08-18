using ErpApp.Domain.Crm;
using ErpApp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Crm;

public sealed class DealAssigneeConfiguration : IEntityTypeConfiguration<DealAssignee>
{
    public void Configure(EntityTypeBuilder<DealAssignee> builder)
    {
        builder.ToTable("DealAssignees", schema: "crm");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DealId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();

        builder.HasIndex(x => new { x.DealId, x.UserId }).IsUnique();

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
