using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 37 -- posts the cost catch-up <see cref="IStockLedgerService.IncrementAsync"/> reports
/// when a receipt pays off a shortfall layer, so the general ledger's Inventory balance keeps
/// agreeing with the FIFO layers.
///
/// <para><b>Why this is a second GL entry rather than a leg on the receipt's own.</b> Every one of
/// the eleven places that call <c>IncrementAsync</c> -- a Purchase Bill, a Credit Note, a Warehouse
/// Transfer's destination, an Inventory Adjustment increase, a Production Journal's output, an
/// Opening Stock line, and five void paths -- can find itself covering a debt, and each builds its
/// entry through a different <c>IGlPostingRule</c> with a different input record. Threading one
/// more optional amount through six posting inputs, six rules and their tests would have put the
/// same two lines in six places (the copy-N+1 trap, phase 33). Phase 36 removed the only reason
/// not to do it the other way: <c>(SourceDocumentType, SourceDocumentId)</c> was never unique, and
/// <c>SourceDocumentGlEntries</c> now reads and reverses <i>every</i> entry a document posted, so a
/// second entry against the receipt is reversed with it and shown with it.</para>
///
/// <para><b>Why the Inventory Adjustment account.</b> The catch-up is the difference between what a
/// sale (or a production run, or a transfer) was told the goods cost and what they turned out to
/// cost. Charging it back to whichever account originally took the wrong figure would mean
/// remembering, per shortfall layer, which document consumed it and which account that document's
/// posting rule debited -- and one shortfall layer can be filled by several receipts, across
/// periods. The tenant's Inventory Adjustment account already exists for exactly this shape of
/// entry ("inventory is worth something other than the transactions say"), is seeded, is
/// configurable on the Accounting Defaults screen, and lands in the Income Statement next to the
/// cost it is correcting. The alternative -- a new tenant-default account -- would owe a migration,
/// a screen and a backfill for a figure that is zero on every tenant that never oversells (and
/// phase 29's lesson is that an unscreened default account is an unreachable one).</para>
/// </summary>
internal static class StockCostCatchUp
{
    /// <summary>
    /// Adds the balanced entry for <paramref name="amount"/> and returns it, or returns null when
    /// there is nothing to post. A positive amount means the covering receipt cost more than the
    /// shortfall was issued at, so stock on hand is worth that much less than the receipt implies:
    /// Debit Inventory Adjustment, Credit Inventory. A negative amount is the mirror.
    ///
    /// <para>Called after the document's own entry is built, before the single
    /// <c>SaveChangesAsync</c> -- same transaction boundary the stock mutation itself observes.</para>
    /// </summary>
    public static async Task<GlJournalEntry?> PostAsync(
        IAppDbContext db,
        Guid organizationId,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        Guid? locationId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (amount == 0)
        {
            return null;
        }

        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var inventoryAccountId = settings.DefaultInventoryAccountId
            ?? throw new ConflictException(
                "Default Inventory account is not configured. Set it under Accounting Defaults before approving this document.");
        var adjustmentAccountId = settings.DefaultInventoryAdjustmentAccountId
            ?? throw new ConflictException(
                "Default Inventory Adjustment account is not configured. Set it under Accounting Defaults before approving this document.");

        var lines = amount > 0
            ? new List<GlLineInput>
            {
                new(adjustmentAccountId, amount, 0m),
                new(inventoryAccountId, 0m, amount),
            }
            : new List<GlLineInput>
            {
                new(inventoryAccountId, -amount, 0m),
                new(adjustmentAccountId, 0m, -amount),
            };

        var entry = GlJournalEntry.Post(organizationId, sourceDocumentType, sourceDocumentId, lines, locationId);
        db.GlJournalEntries.Add(entry);

        return entry;
    }
}
