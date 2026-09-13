using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Configuration;

public sealed class CustomTemplateConfiguration : IEntityTypeConfiguration<CustomTemplate>
{
    public void Configure(EntityTypeBuilder<CustomTemplate> builder)
    {
        builder.ToTable("CustomTemplates", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
        // Phase 39: rich text now, so the 4,000-character cap has to go. Markup roughly doubles the
        // length of the same prose, and the cap was set when a template body was a plain textarea --
        // leaving it would turn "add bold to your terms" into a save that fails. RichText.MaxLength
        // is the bound that replaces it, enforced in the validator where it can name the field.
        builder.Property(x => x.Body).IsRequired();
        builder.Property(x => x.IsDefault).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.Type, x.Name }).IsUnique();
    }
}
