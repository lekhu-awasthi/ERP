using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Communications;

public sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("EmailTemplates", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Context).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Body).IsRequired();

        // 320 is the RFC maximum for one address; CC/BCC hold a comma-separated list, so they get
        // room for a realistic handful rather than one.
        builder.Property(x => x.ReplyTo).HasMaxLength(320);
        builder.Property(x => x.Cc).HasMaxLength(2000);
        builder.Property(x => x.Bcc).HasMaxLength(2000);

        builder.Property(x => x.IsDefault).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // A template name is unique within its context, not across the tenant: "Notification" is a
        // perfectly reasonable name for both an Invoice and a Quotation template, and the create
        // handler's duplicate check is scoped the same way.
        builder.HasIndex(x => new { x.OrganizationId, x.Context, x.Name }).IsUnique();

        builder.HasIndex(x => new { x.OrganizationId, x.Context });
    }
}
