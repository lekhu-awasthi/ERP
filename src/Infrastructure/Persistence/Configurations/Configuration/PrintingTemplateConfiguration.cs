using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Configuration;

public sealed class PrintingTemplateConfiguration : IEntityTypeConfiguration<PrintingTemplate>
{
    public void Configure(EntityTypeBuilder<PrintingTemplate> builder)
    {
        builder.ToTable("PrintingTemplates", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.IsDefault).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Phase 73. Standard is member 0 and is what every existing row printed, so the scaffolded 0 default is
        // true of them; the layout is stored by name all the same, like DocumentType beside it.
        builder.Property(x => x.Layout).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Delimited strings, as CustomFieldDefinition.ApplicableDocumentTypes: elements by name (an inserted enum
        // member never re-reads a row), and the empty string -- nothing hidden, no custom fields -- is what every
        // existing row printed. Each list is replaced wholesale by UpdateLayout, hence the comparers.
        builder.Property(x => x.HiddenElements)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Length == 0
                    ? new List<PrintElement>()
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<PrintElement>).ToList(),
                new ValueComparer<IReadOnlyList<PrintElement>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, e) => HashCode.Combine(hash, e)),
                    v => v.ToList()))
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.PrintedCustomFieldIds)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Length == 0
                    ? new List<Guid>()
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList(),
                new ValueComparer<IReadOnlyList<Guid>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, e) => HashCode.Combine(hash, e)),
                    v => v.ToList()))
            .IsRequired();

        builder.Property(x => x.FooterText).HasMaxLength(PrintingTemplate.FooterTextMaxLength);
        builder.Property(x => x.CompactMargins).IsRequired();
        // Phase 95 -- off, true of every existing template: no bill changes until a company asks.
        builder.Property(x => x.PrintSerialNumbers).IsRequired().HasDefaultValue(false);

        builder.HasIndex(x => new { x.OrganizationId, x.DocumentType, x.Name }).IsUnique();
    }
}
