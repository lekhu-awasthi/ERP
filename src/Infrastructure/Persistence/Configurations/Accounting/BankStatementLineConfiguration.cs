using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
{
    public void Configure(EntityTypeBuilder<BankStatementLine> builder)
    {
        builder.ToTable("BankStatementLines", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.BankAccountId).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ImportJobId);

        // One signed column, because StatementAmount is one signed value -- see that type for why
        // this diverges from the reference product's dr_amount/cr_amount pair. FromSigned is the
        // converter's other half and is the only place a raw signed decimal may become an amount.
        builder.Property(x => x.Amount)
            .HasConversion(v => v.Signed, v => StatementAmount.FromSigned(v))
            .HasPrecision(18, 2)
            .IsRequired();

        // No index is declared here, and that is a decision rather than an omission.
        //
        // TenantIndexConvention recognises Date by name and derives the two this table needs:
        // (OrganizationId, Date) for the date-range filter and (OrganizationId, CreatedAt DESC)
        // for the default ordering. The obvious third candidate is (OrganizationId, BankAccountId,
        // Date), because this list is only ever read one account at a time -- and it is not added,
        // because nothing has measured it. Phase 34c's rule is that an index is added against a
        // number and that an index added for one path changes the plan for every other path on the
        // table; phase 50 restated it after a reasoned-about index took a sibling tab from 2,143
        // logical reads to 83,308. A tenant holds a handful of cash-and-bank accounts, so the
        // account predicate's selectivity over a date range is small, and "obviously better" is
        // exactly the claim those two phases say not to ship unmeasured.
        //
        // The re-entry condition, so this is checkable rather than a shrug: a tenant with several
        // accounts and enough statement history for the list's first page to cost more than the
        // count, measured through tools/scale on logical reads (never the wall clock, phase 50).

        // There is deliberately no unique index, and this is the decision a re-import makes people
        // want. A bank statement has no natural key: two identical ATM withdrawals on one day are
        // two real lines, so any uniqueness rule wide enough to catch a duplicated upload also
        // rejects genuine data -- phase 52's "a conversion factor below one is ordinary" in another
        // key. The reference product accepts duplicate rows silently (verified: two identical rows
        // both came back valid). We do not go further than refusing to invent a key that is not
        // there; what we add instead is ImportJobId, so a doubled upload is one deletion rather
        // than a hunt through the list.
        //
        // No foreign key to Account either. The relationship is real, but the importer resolves and
        // checks the account once per run rather than per row, and a cascade from the chart of
        // accounts into an external record of what a bank did is not a delete anybody wants.
    }
}
