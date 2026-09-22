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

        // Phase 57 -- the index phase 56 owed a number for, and the number says something different
        // from what 56 expected.
        //
        // MEASURED on tools/scale's 50k-invoice dataset, on logical reads (tools/scale's
        // comparison-phase57.md; probe-phase57-io.sql is the script, and every statement in it is
        // the SQL EF actually issues, lifted from the API's own log). Phase 56 predicted that if
        // anything here needed an index it would be ReconciliationId. It does not, and the pane was
        // never slow for that reason:
        //
        //   the matcher's right-hand pane, page 1                153,470 logical reads
        //   with (AccountId, ReconciliationId) INCLUDE(...)          554
        //   with (AccountId) INCLUDE(..., ReconciliationId)          553
        //
        // ReconciliationId in the *key* is worth one logical read. The whole 277x is the index
        // being COVERING on AccountId: EF's automatic foreign-key index is a single narrow column,
        // and with a quarter of the table matching one account the optimizer preferred a full scan
        // of GlLines to fifty thousand key lookups -- on the matcher, on the Book Statement and on
        // the report's own balance alike, every one of them since long before phase 56 existed.
        //
        // So this REPLACES the automatic FK index rather than joining it: same leading key, same
        // index count, nothing new to maintain on an append-only table. Every path measured
        // improves and none regresses -- including the two this phase did not target (Book
        // Statement 153,470 -> 553, and the tenant-wide Trial Balance 10,758 -> 6,906), which is
        // phase 34c's rule that an index added for one access path changes the plan for every other
        // path on the same table, asked and answered rather than assumed.
        //
        // The INCLUDE list is exactly what BankBookTransactionReader projects plus what every GL
        // report sums; a column added to that projection has to be added here, or the covering
        // property is silently lost and the 277x goes with it.
        builder
            .HasIndex(x => x.AccountId)
            .IncludeProperties(x => new { x.GlJournalEntryId, x.Debit, x.Credit, x.ReconciliationId });

        // Phase 56 -- nullable, and with no foreign-key constraint.
        //
        // Not the key of any index, and phase 57 measured that rather than reasoning about it: the
        // reconciliation module never asks "what is reconciled?" across the chart of accounts, only
        // "what is still unmatched on THIS bank account", so the reconciliation state is a residual
        // predicate over a set the account has already narrowed. It is in the INCLUDE list above,
        // because the pane reads it.
        //
        // No FK constraint either, and that one is not laziness: the delete path releases both
        // sides in the same SaveChanges (DeleteBankReconciliationCommandHandler), so a constraint
        // would buy nothing a handler is not already doing -- while a cascade would mean deleting a
        // reconciliation deletes GL lines, which is the one thing that must never happen to an
        // append-only fact row.
        builder.Property(x => x.ReconciliationId);
    }
}
