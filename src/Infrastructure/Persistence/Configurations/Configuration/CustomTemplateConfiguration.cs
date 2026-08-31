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
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.IsDefault).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.Type, x.Name }).IsUnique();
    }
}
