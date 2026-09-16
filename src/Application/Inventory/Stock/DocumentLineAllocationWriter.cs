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
        bool isReceipt,
        CancellationToken cancellationToken)
    {
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
                product, line.BatchNo is null ? null : Guid.Empty, serials, isReceipt);

            // ValidateLineAllocation takes an id it cannot have yet, so "is a batch named" is
            // expressed as a sentinel above. The one branch that sentinel gets wrong is the
            // batch-tracked receipt with a blank number, which reads as "named" only if BatchNo is
            // non-null -- and a blank string is not a name.
            if (product.BatchTracking && isReceipt && string.IsNullOrWhiteSpace(line.BatchNo))
            {
                message = $"'{product.Name}' is batch-tracked, so a line receiving it must name a batch.";
            }

            message ??= StockTrackingRules.ValidateSerialCount(product, line.Quantity, serials.Count);

            if (message is not null)
            {
                failures.Add(new ValidationFailure($"Lines[{i}]", message));
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

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
                            $"There is no batch '{batchNo}' for this product, so there is none of it to issue."),
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
