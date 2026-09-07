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

        // Phase 32 -- the counter key gained a third column. LocationId null is the settings row
        // (one per org+type, carrying Prefix/Mode/the flags, and the shared counter while
        // LocationWiseNumbering is off); a non-null value is one branch's own counter.
        //
        // HasFilter(null) is load-bearing and must not be removed. EF Core *automatically* scaffolds
        // `filter: "[LocationId] IS NOT NULL"` on a unique index over a nullable column -- normally
        // the right thing, and the thing CLAUDE.md's own gotcha asks for. Here it is exactly wrong:
        // SQL Server treating NULLs as equal is the invariant being bought, because it is what admits
        // one and only one settings row (LocationId null) per (org, type). With EF's default filter
        // those rows fall outside the index entirely, a tenant can end up with two settings rows and
        // two competing counters, and DocumentNumberGenerator's lazy-create race -- which relies on a
        // concurrent loser's INSERT violating this index -- silently stops being guarded for them.
        //
        // Still guards that race, now per location.
        builder.HasIndex(x => new { x.OrganizationId, x.DocumentType, x.LocationId })
            .IsUnique()
            .HasFilter(null);

        // Restrict, matching every other document->location FK added this phase.
        builder.HasOne<Domain.Tenancy.BillingLocation>().WithMany().HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
