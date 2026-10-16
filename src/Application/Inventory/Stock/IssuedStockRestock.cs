using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 93 -- what an issue took, per batch and serial, and how putting it back divides among the document's lines.
///
/// <para><b>The bug.</b> A line that named no batch -- a till sale, which has no batch picker (phase 91 § 5), or an ERP
/// issue saved before phase 93 made the batch required -- walks every batch oldest-first. Its void restocked the whole
/// quantity as one un-batched layer (phase 92 § 5), and a till refund of it copied the sale line's empty batch the same
/// way: stock of a batch-tracked product belonging to no batch, which is the bucket phase 51's Decision D refuses on a
/// receipt and which reconciles against nothing.</para>
///
/// <para><b>The source.</b> The line does not know which batches the walk took; the document's own
/// <see cref="StockMovement"/> rows do (phase 51 writes one per batch, serial and cost the walk relieved, stamped with
/// both). They are per document, not per line, so two lines of one product share them: <see cref="Plan"/> gives each
/// line its named batch first and lets the lines that named none walk what is left, in the order it was taken. A
/// product that is not batch-tracked has one un-batched pool and so takes exactly one part per line -- the path every
/// line took before this phase, down to the number of rows written.</para>
///
/// <para>The cost a part is restocked at is the caller's, unchanged (the line's own COGS or consumed cost), so the GL
/// does not move: phase 92's rule for serials, kept for batches.</para>
/// </summary>
public static class IssuedStockRestock
{
    /// <summary>One (product, batch, serial) a document issued, net of what it has put back, in primary units.</summary>
    public sealed record IssuedRow(Guid ProductId, Guid? BatchId, string? SerialNo, decimal Quantity);

    /// <summary>A line to put back: its primary quantity, the batch it named (if any) and the serials it names.</summary>
    public sealed record RestockLine(Guid ProductId, decimal Quantity, Guid? BatchId, IReadOnlyList<string> SerialNumbers);

    /// <summary>One <c>IncrementLineAsync</c> call's worth of a line: a quantity into one batch, with its serials.</summary>
    public sealed record RestockPart(Guid? BatchId, decimal Quantity, IReadOnlyList<string> SerialNumbers);

    /// <summary>What <paramref name="documentId"/> issued, net of what it has put back since (Out less In), by product,
    /// batch and serial, in the order first taken.</summary>
    public static async Task<IReadOnlyList<IssuedRow>> OfDocumentAsync(
        IAppDbContext db, Guid organizationId, DocumentType documentType, Guid documentId,
        CancellationToken cancellationToken)
    {
        var rows = await db.StockMovements
            .Where(x => x.OrganizationId == organizationId && x.SourceDocumentType == documentType
                && x.SourceDocumentId == documentId && x.Quantity > 0)
            .Select(x => new { x.ProductId, x.BatchId, x.SerialNo, x.Direction, x.Quantity, x.CreatedAt })
            .ToListAsync(cancellationToken);

        return Net(rows.Select(x => (x.ProductId, x.BatchId, x.SerialNo,
            x.Direction == StockMovementDirection.Out ? x.Quantity : -x.Quantity, x.CreatedAt)));
    }

    /// <summary>What <paramref name="invoiceId"/> issued that no credit note returning it has brought back: the sale's
    /// net issue less every such note's net restock (a voided note's restock is reversed, so it gives its share up),
    /// leaving out <paramref name="excludingCreditNoteId"/> -- the note being approved, whose own restock is the
    /// question.</summary>
    public static async Task<IReadOnlyList<IssuedRow>> OutstandingOnInvoiceAsync(
        IAppDbContext db, Guid organizationId, Guid invoiceId, Guid? excludingCreditNoteId,
        CancellationToken cancellationToken)
    {
        var notes = db.CreditNotes
            .Where(x => x.OrganizationId == organizationId && x.ReferrerType == DocumentType.Invoice
                && x.ReferrerId == invoiceId && x.Id != excludingCreditNoteId)
            .Select(x => x.Id);

        var rows = await db.StockMovements
            .Where(x => x.OrganizationId == organizationId && x.Quantity > 0
                && ((x.SourceDocumentType == DocumentType.Invoice && x.SourceDocumentId == invoiceId)
                    || (x.SourceDocumentType == DocumentType.CreditNote && notes.Contains(x.SourceDocumentId))))
            .Select(x => new { x.ProductId, x.BatchId, x.SerialNo, x.Direction, x.Quantity, x.CreatedAt })
            .ToListAsync(cancellationToken);

        return Net(rows.Select(x => (x.ProductId, x.BatchId, x.SerialNo,
            x.Direction == StockMovementDirection.Out ? x.Quantity : -x.Quantity, x.CreatedAt)));
    }

    private static List<IssuedRow> Net(
        IEnumerable<(Guid ProductId, Guid? BatchId, string? SerialNo, decimal Signed, DateTimeOffset At)> rows) =>
        [.. rows
            .GroupBy(x => (x.ProductId, x.BatchId, Serial: x.SerialNo?.ToUpperInvariant()))
            .Select(g => (Row: new IssuedRow(g.Key.ProductId, g.Key.BatchId, g.First().SerialNo, g.Sum(x => x.Signed)),
                First: g.Min(x => x.At)))
            .Where(x => x.Row.Quantity > 0)
            .OrderBy(x => x.First)
            .Select(x => x.Row)];

    /// <summary>
    /// Divides <paramref name="issued"/> among <paramref name="lines"/>, positionally: a serial goes back under the batch
    /// it left from; a line that named a batch takes it; a line that named none walks what is left of its product in
    /// the order it was taken, and anything the movements do not cover (stock that predates phase 51's stamping) goes
    /// back un-batched, as it always did.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<RestockPart>> Plan(
        IReadOnlyList<RestockLine> lines, IReadOnlyList<IssuedRow> issued)
    {
        var pool = issued.Select(x => new PoolRow(x.ProductId, x.BatchId, x.SerialNo, x.Quantity)).ToList();
        var parts = lines.Select(_ => new List<RestockPart>()).ToArray();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.SerialNumbers.Count == 0)
            {
                continue;
            }

            foreach (var group in line.SerialNumbers.GroupBy(serial =>
            {
                var row = pool.FirstOrDefault(x => x.ProductId == line.ProductId && x.Remaining > 0
                    && string.Equals(x.SerialNo, serial.Trim(), StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    return line.BatchId;
                }

                row.Remaining -= 1m;
                return row.BatchId;
            }))
            {
                parts[i].Add(new RestockPart(group.Key, group.Count(), [.. group]));
            }
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.SerialNumbers.Count > 0 || line.BatchId is null)
            {
                continue;
            }

            Take(pool, line.ProductId, line.Quantity, x => x.BatchId == line.BatchId);
            parts[i].Add(new RestockPart(line.BatchId, line.Quantity, []));
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.SerialNumbers.Count > 0 || line.BatchId is not null)
            {
                continue;
            }

            var left = line.Quantity;
            foreach (var row in pool.Where(x => x.ProductId == line.ProductId && x.SerialNo is null))
            {
                if (left <= 0)
                {
                    break;
                }

                var take = Math.Min(left, row.Remaining);
                if (take <= 0)
                {
                    continue;
                }

                row.Remaining -= take;
                left -= take;
                Add(parts[i], row.BatchId, take);
            }

            if (left > 0)
            {
                Add(parts[i], null, left);
            }
        }

        return parts;
    }

    /// <summary>Puts one line's <paramref name="parts"/> back at <paramref name="unitCost"/> and returns the phase-37
    /// cost catch-up summed across them, which the caller posts.</summary>
    public static async Task<decimal> PutBackAsync(
        IStockLedgerService ledger,
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        IReadOnlyList<RestockPart> parts,
        decimal unitCost,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken,
        Guid? locationId)
    {
        var catchUp = 0m;
        foreach (var part in parts)
        {
            catchUp += await LineStockAllocator.IncrementLineAsync(
                ledger, organizationId, productId, warehouseId, PrimaryQuantity.AlreadyPrimary(part.Quantity), unitCost,
                part.BatchId, part.SerialNumbers, sourceDocumentType, sourceDocumentId, transactionDate,
                cancellationToken, locationId);
        }

        return catchUp;
    }

    private static void Take(List<PoolRow> pool, Guid productId, decimal quantity, Func<PoolRow, bool> match)
    {
        foreach (var row in pool.Where(x => x.ProductId == productId && x.SerialNo is null && match(x)))
        {
            var take = Math.Min(quantity, row.Remaining);
            row.Remaining -= take;
            quantity -= take;
            if (quantity <= 0)
            {
                return;
            }
        }
    }

    private static void Add(List<RestockPart> parts, Guid? batchId, decimal quantity)
    {
        var index = parts.FindIndex(x => x.BatchId == batchId);
        if (index < 0)
        {
            parts.Add(new RestockPart(batchId, quantity, []));
        }
        else
        {
            parts[index] = parts[index] with { Quantity = parts[index].Quantity + quantity };
        }
    }

    private sealed class PoolRow(Guid productId, Guid? batchId, string? serialNo, decimal remaining)
    {
        public Guid ProductId { get; } = productId;

        public Guid? BatchId { get; } = batchId;

        public string? SerialNo { get; } = serialNo;

        public decimal Remaining { get; set; } = remaining;
    }
}
