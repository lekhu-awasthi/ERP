using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Configuration;

public sealed class DocumentNumberingRuleConfiguration : IEntityTypeConfiguration<DocumentNumberingRule>
{
    public void Configure(EntityTypeBuilder<DocumentNumberingRule> builder)
    {
        builder.ToTable("DocumentNumberingRules", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Prefix).HasMaxLength(20).IsRequired();
        builder.Property(x => x.NextNumber).IsRequired();
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ResetEveryFiscalYear).IsRequired();
        builder.Property(x => x.IncludeFiscalYearInCode).IsRequired();
        builder.Property(x => x.LocationWiseNumbering).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Protects only the admin-edited settings fields (Prefix/Mode/...) via EF's optimistic
        // concurrency -- NextNumber itself is never written through SaveChanges/this row-version;
        // IDocumentNumberGenerator increments it with a raw-SQL atomic UPDATE...OUTPUT that
        // bypasses the change tracker entirely (see phase-2-status.md's numbering-concurrency
        // scope decision for why RowVersion alone isn't safe for the increment).
        builder.Property(x => x.RowVersion).IsRowVersion();

        // Guards both the "one row per (org, doc type)" invariant and the lazy-create race in
        // DocumentNumberGenerator (a concurrent loser's INSERT hits this and retries the UPDATE).
        builder.HasIndex(x => new { x.OrganizationId, x.DocumentType }).IsUnique();
    }
}
