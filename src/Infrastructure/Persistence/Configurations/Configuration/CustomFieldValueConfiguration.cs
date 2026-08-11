using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Configuration;

public sealed class CustomFieldValueConfiguration : IEntityTypeConfiguration<CustomFieldValue>
{
    public void Configure(EntityTypeBuilder<CustomFieldValue> builder)
    {
        builder.ToTable("CustomFieldValues", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.FieldDefinitionId).IsRequired();
        builder.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.DocumentId).IsRequired();
        builder.Property(x => x.Value).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ValueType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.DocumentType, x.DocumentId });

        builder.HasOne<CustomFieldDefinition>()
            .WithMany()
            .HasForeignKey(x => x.FieldDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
