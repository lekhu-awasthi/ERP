using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class GlLineConfiguration : IEntityTypeConfiguration<GlLine>
{
    public void Configure(EntityTypeBuilder<GlLine> builder)
    {
        builder.ToTable("GlLines", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Debit).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Credit).HasPrecision(18, 4).IsRequired();

        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);

        // Phase 56 -- nullable, un-indexed, and with no foreign-key constraint.
        //
        // NOT indexed, because nothing has measured it. Every read of this column is already
        // seeking on AccountId: the reconciliation module never asks "what is reconciled?" across
        // the chart of accounts, only "what is still unmatched on THIS bank account", so the
        // reconciliation state is a residual predicate over a set the account has already narrowed
        // to one cash-and-bank account's postings. GlLines is a large table on any real tenant and
        // is read by eleven reports, and phase 34c's rule is that an index added for one access
        // path changes the plan for every other path on the same table -- phase 50 restated it
        // after a reasoned-about index took a sibling tab from 2,143 logical reads to 83,308.
        // RE-ENTRY CONDITION: the matcher's right-hand pane measured on tools/scale's 50k-invoice
        // dataset, on logical reads and never the wall clock (phase 50).
        //
        // No FK constraint either, and that one is not laziness: the delete path releases both
        // sides in the same SaveChanges (DeleteBankReconciliationCommandHandler), so a constraint
        // would buy nothing a handler is not already doing -- while a cascade would mean deleting a
        // reconciliation deletes GL lines, which is the one thing that must never happen to an
        // append-only fact row.
        builder.Property(x => x.ReconciliationId);
    }
}
