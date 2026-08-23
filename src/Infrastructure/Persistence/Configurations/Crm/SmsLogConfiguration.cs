using ErpApp.Domain.Contacts;
using ErpApp.Domain.Crm;
using ErpApp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Crm;

public sealed class SmsLogConfiguration : IEntityTypeConfiguration<SmsLog>
{
    public void Configure(EntityTypeBuilder<SmsLog> builder)
    {
        builder.ToTable("SmsLogs", schema: "crm");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.BatchId).IsRequired();
        builder.Property(x => x.ContactId).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PhoneNumber).HasMaxLength(30).IsRequired();
        builder.Property(x => x.CreditsUsed).IsRequired();
        builder.Property(x => x.SentByUserId).IsRequired();
        builder.Property(x => x.SentAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.ContactId });
        builder.HasIndex(x => new { x.OrganizationId, x.BatchId });

        builder.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SmsTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.SentByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
