using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Inventory;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 51 -- turns what a client sent on a document line (a batch <i>number</i>, two optional
/// dates, and a list of serial numbers) into what the aggregate stores (a <c>BatchId</c>, and
/// <see cref="DocumentLineSerial"/> rows).
///
/// <para><b>Why the client sends a number and not an id.</b> On a Purchase Bill the batch usually
/// does not exist yet -- naming <c>BATCH123</c> on a receipt is how a batch comes into being, which
/// is what the 2026-09-16 read shows: one <c>Item Batch</c> column on both sides, so a batch is
/// created on receipt and consumed on issue through the same control. A client that had to create
/// the batch first would need a second screen the reference product does not have.</para>
///
/// <para><b>Mint on receipt, resolve on issue.</b> A receipt may name a batch that does not exist
/// and it is created; an issue naming one that does not exist is a 400, because there is no stock
/// of it to relieve and silently minting one would produce an empty bucket. That is the same
/// asymmetry <c>StockTrackingRules</c> applies to whether the field is required at all.</para>
///
/// <para>A batch is minted at <b>Create/Update</b> rather than at Approve, unlike a document
/// number. A document number is drawn at Approve because it is a statutory sequence that a
/// discarded draft must not consume; a batch is master data with no sequence, and a draft that
/// names it needs it to exist so the draft can be read back and edited. An unused batch is an
/// identity with no layers -- harmless, and what a user means by "I have registered batch B".</para>
/// </summary>
public static class DocumentLineAllocationWriter
{
    /// <summary>What a client sent for one line, in the order the lines were sent.</summary>
    public sealed record LineAllocationInput(
        Guid ProductId,
        decimal Quantity,
        string? BatchNo,
        DateOnly? ManufactureDate,
        DateOnly? ExpiryDate,
        IReadOnlyList<string>? SerialNumbers);

    /// <summary>
    /// Validates every line against its product's flags and resolves (or mints) the batches.
    /// Returns one nullable <c>BatchId</c> per input line, positionally.
    ///
    /// <para>Throws <see cref="ValidationException"/> -- a 400 naming the offending line -- rather
    /// than letting a Domain invariant surface as a 500 (phase 39).</para>
    /// </summary>
    public static async Task<IReadOnlyList<Guid?>> ResolveBatchesAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyList<LineAllocationInput> lines,
        StockLineKind kind,
        CancellationToken cancellationToken)
    {
        var isReceipt = kind == StockLineKind.Receipt;

        if (lines.Count == 0)
        {
            return [];
        }

        var productIds = lines.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var failures = new List<ValidationFailure>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (!products.TryGetValue(line.ProductId, out var product))
            {
                // Existence is somebody else's job (ProductVariantRules) and has already run, or is
                // about to. Skipping keeps the two error reports from contradicting each other.
                continue;
            }

            var serials = line.SerialNumbers ?? [];

            var message = StockTrackingRules.ValidateLineAllocation(
                product, line.BatchNo is null ? null : Guid.Empty, serials, kind);

            // ValidateLineAllocation takes an id it cannot have yet, so "is a batch named" is
            // expressed as a sentinel above. The one branch that sentinel gets wrong is the
            // batch-tracked line with a blank number, which reads as "named" only if BatchNo is
            // non-null -- and a blank string is not a name.
            if (product.BatchTracking && string.IsNullOrWhiteSpace(line.BatchNo))
            {
                message = StockTrackingRules.BatchRequiredMessage(product.Name, kind);
            }

            message ??= StockTrackingRules.ValidateSerialCount(product, line.Quantity, serials.Count);

            if (message is not null)
            {
                failures.Add(new ValidationFailure($"Lines[{i}]", message));
            }
        }

        if (failures.Count == 0)
        {
            failures.AddRange(await FindSerialConflictsAsync(db, organizationId, lines, products, isReceipt, cancellationToken));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await ResolveNamedAsync(db, organizationId, lines, isReceipt, cancellationToken);
    }

    /// <summary>
    /// Phase 93 -- the batch half of <see cref="ResolveBatchesAsync"/>, for a credit or debit note, whose serials have
    /// their own rule (<c>CreditNoteSerials</c>, <c>DebitNoteSerials</c>: a draft may still be naming them, and a
    /// converted note names only its source's units). Phase 51 gave both notes' lines a <c>BatchId</c> and nothing
    /// wrote one, so a debit note relieved whichever batch FIFO reached first and a returned sale came back into no
    /// batch at all. A tracked line that moves stock names its batch (the vendor refuses a blank one on both notes);
    /// a line that moves none (<paramref name="movesStock"/> false: a credit note not converted from its invoice)
    /// names none. Returns one nullable <c>BatchId</c> per line, positionally; a 400 naming the line otherwise.
    /// </summary>
    public static async Task<IReadOnlyList<Guid?>> ResolveNoteBatchesAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyList<(Guid ProductId, string? BatchNo)> lines,
        StockLineKind kind,
        bool movesStock,
        CancellationToken cancellationToken)
    {
        var productIds = lines.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var failures = new List<ValidationFailure>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!products.TryGetValue(lines[i].ProductId, out var product))
            {
                continue;
            }

            var named = !string.IsNullOrWhiteSpace(lines[i].BatchNo);
            var message = (named, product.BatchTracking, movesStock) switch
            {
                (true, false, _) => $"'{product.Name}' is not batch-tracked, so a line for it cannot name a batch.",
                (true, true, false) => $"Line {i + 1}: this note returns no goods, so it names no batch.",
                (false, true, true) when product.Type == ProductType.Goods =>
                    StockTrackingRules.BatchRequiredMessage(product.Name, kind),
                _ => null,
            };

            if (message is not null)
            {
                failures.Add(new ValidationFailure($"Lines[{i}]", message));
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // A note never mints: a converted credit note returns a batch its invoice issued and a debit note one on the
        // shelf, so a number that does not exist is a typo, refused by name.
        return await ResolveNamedAsync(
            db, organizationId,
            [.. lines.Select(x => new LineAllocationInput(x.ProductId, 0m, x.BatchNo, null, null, null))],
            isReceipt: false, cancellationToken);
    }

    /// <summary>Resolves (on a receipt, mints) the batch each line names by number; null where it names none.</summary>
    private static async Task<IReadOnlyList<Guid?>> ResolveNamedAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyList<LineAllocationInput> lines,
        bool isReceipt,
        CancellationToken cancellationToken)
    {
        var resolved = new Guid?[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line.BatchNo))
            {
                continue;
            }

            var batchNo = line.BatchNo.Trim();

            var batch = await db.ProductBatches.FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.ProductId == line.ProductId && x.BatchNo == batchNo,
                cancellationToken);

            if (batch is null)
            {
                if (!isReceipt)
                {
                    throw new ValidationException(
                    [
                        new ValidationFailure(
                            $"Lines[{i}]",
                            $"There is no batch '{batchNo}' for this product, so there is none of it to move."),
                    ]);
                }

                batch = ProductBatch.Create(
                    organizationId, line.ProductId, batchNo, line.ManufactureDate, line.ExpiryDate);
                db.ProductBatches.Add(batch);
            }
            else if (isReceipt)
            {
                // A later receipt of the same batch may carry dates the first one did not -- the
                // goods arrive, the paperwork follows. Filling a blank is allowed; changing a date
                // already set is refused inside the aggregate, because the batch is attached to
                // layers that were costed and reported under it.
                batch.FillMissingDates(line.ManufactureDate, line.ExpiryDate);
            }

            resolved[i] = batch.Id;
        }

        return resolved;
    }

    /// <summary>
    /// Phase 92 -- the serial rules one line cannot see: a unit named on two lines of the document, and, on a receipt,
    /// a unit the product already holds in stock. Both reached phase 51's unique index at Approve as a 500; the vendor
    /// validates each serial as it is typed, so a draft hears it here as a 400 naming the line, and the ledger's 409
    /// stays the backstop for whatever changes between the save and the approval.
    /// </summary>
    private static async Task<List<ValidationFailure>> FindSerialConflictsAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyList<LineAllocationInput> lines,
        IReadOnlyDictionary<Guid, Product> products,
        bool isReceipt,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        var named = new HashSet<(Guid ProductId, string SerialNo)>();

        for (var i = 0; i < lines.Count; i++)
        {
            // Compared as the database compares them (case-insensitively); a repeat within one line is that line's own
            // message, from ValidateLineAllocation.
            foreach (var serial in (lines[i].SerialNumbers ?? []).Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!named.Add((lines[i].ProductId, serial.ToUpperInvariant())))
                {
                    failures.Add(new ValidationFailure(
                        $"Lines[{i}]", $"Serial number '{serial}' is named on two lines of this document."));
                }
            }
        }

        if (failures.Count > 0 || !isReceipt)
        {
            return failures;
        }

        foreach (var productLines in lines.Select((line, i) => (Line: line, Index: i))
            .Where(x => x.Line.SerialNumbers is { Count: > 0 } && products.ContainsKey(x.Line.ProductId))
            .GroupBy(x => x.Line.ProductId))
        {
            var inStock = await SerialStock.InStockAsync(
                db, organizationId, productLines.Key,
                [.. productLines.SelectMany(x => x.Line.SerialNumbers!)], cancellationToken);

            foreach (var (line, index) in productLines)
            {
                var held = inStock.FirstOrDefault(s =>
                    line.SerialNumbers!.Any(n => string.Equals(n.Trim(), s, StringComparison.OrdinalIgnoreCase)));
                if (held is not null)
                {
                    failures.Add(new ValidationFailure(
                        $"Lines[{index}]",
                        $"Serial number '{held}' of '{products[productLines.Key].Name}' is already in stock, so it "
                            + "cannot be received again."));
                }
            }
        }

        return failures;
    }

    /// <summary>
    /// Replaces a document's serial rows wholesale: removes every row for
    /// <paramref name="removedLineIds"/> and writes the new ones.
    ///
    /// <para>Wholesale because that is what an Update to a line-bearing aggregate already does --
    /// <c>ClearLines</c>, <c>RemoveRange</c>, <c>AddRange</c> -- and a serial row keyed by a line id
    /// that no longer exists is an orphan no query would ever find. Going through the
    /// <see cref="IAppDbContext.DocumentLineSerials"/> set directly rather than an encapsulated
    /// collection is the same remedy phase 4 and phase 24 both needed, for the same reason.</para>
    /// </summary>
    public static async Task ReplaceSerialsAsync(
        IAppDbContext db,
        Guid organizationId,
        DocumentLineParentType parentType,
        IReadOnlyCollection<Guid> removedLineIds,
        IReadOnlyList<(Guid LineId, IReadOnlyList<string>? SerialNumbers)> lines,
        CancellationToken cancellationToken)
    {
        if (removedLineIds.Count > 0)
        {
            var stale = await db.DocumentLineSerials
                .Where(x => x.OrganizationId == organizationId && x.ParentType == parentType
                    && removedLineIds.Contains(x.ParentLineId))
                .ToListAsync(cancellationToken);

            db.DocumentLineSerials.RemoveRange(stale);
        }

        foreach (var (lineId, serials) in lines)
        {
            foreach (var serialNo in serials ?? [])
            {
                db.DocumentLineSerials.Add(
                    DocumentLineSerial.Create(organizationId, parentType, lineId, serialNo));
            }
        }
    }
}
