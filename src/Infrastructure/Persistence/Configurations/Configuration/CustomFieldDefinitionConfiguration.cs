using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Configuration;

public sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.ToTable("CustomFieldDefinitions", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Delimited string, not a SQL Server array/JSON column -- the spec's "least clever
        // option" preference (architecture-spec.md §3.6), same reasoning applied to
        // CustomFieldValue.Value's storage. A ValueComparer is required since Update() replaces
        // the whole list by reference -- without one, EF's default reference-equality change
        // detection wouldn't reliably notice element-level differences.
        builder.Property(x => x.ApplicableDocumentTypes)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Length == 0
                    ? new List<DocumentType>()
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<DocumentType>).ToList(),
                new ValueComparer<IReadOnlyList<DocumentType>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, e) => HashCode.Combine(hash, e)),
                    v => v.ToList()))
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
    }
}
