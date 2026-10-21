using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing;

/// <summary>
/// Phase 92 -- which serials a debit note may send back to the supplier: phase 91's <c>CreditNoteSerials</c> in mirror.
/// <c>ApproveDebitNoteCommandHandler</c> has read a line's serials since phase 51 and nothing wrote one, so a return of
/// a serial-tracked product relieved whichever units FIFO chose, not the ones going back in the box. The rule:
/// <list type="bullet">
/// <item>a serial may be named only on a serial-tracked product's line, once on the whole note;</item>
/// <item>a note converted from its bill names only units the bill received, and none an approved note of the same bill
/// already sent back (a void gives it up: a sum over the documents not voided, phase 65);</item>
/// <item>any note, converted or standalone, names units in stock at its own warehouse (the user's Decision B: a
/// standalone return names whichever unit is on the shelf, as an issue names one);</item>
/// <item>at Approve, a serial-tracked Goods line names exactly its primary quantity of them.</item>
/// </list>
/// The callers throw the error their door owes: a 400 on a draft's save, a 409 at Approve.
/// </summary>
internal static class DebitNoteSerials
{
    /// <summary>The first problem with the serials <paramref name="serialsByLine"/> names on
    /// <paramref name="debitNote"/>'s lines, with the line number; null when there is none.</summary>
    public static async Task<string?> FindProblemAsync(
        IAppDbContext db,
        DebitNote debitNote,
        IReadOnlyDictionary<Guid, List<string>> serialsByLine,
        bool requireCount,
        CancellationToken cancellationToken)
    {
        var organizationId = debitNote.OrganizationId;
        var productIds = debitNote.Lines.Select(x => x.ProductId).Distinct().ToList();
        // Phase 95 -- serial-tracked as the company's switch lets it be: off overrides the product's own.
        var serialOn = (await StockTrackingSwitches.LoadAsync(db, organizationId, cancellationToken)).Serial;
        var products = await db.Products
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Type, SerialTracking = serialOn && x.SerialTracking })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var lines = debitNote.Lines.Select((line, i) => (Line: line, No: i + 1)).ToList();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (line, no) in lines)
        {
            var serials = serialsByLine.GetValueOrDefault(line.Id) ?? [];
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                continue;
            }

            if (serials.Count > 0 && !product.SerialTracking)
            {
                return $"Line {no}: '{product.Name}' is not serial-tracked, so it names no serial numbers.";
            }

            foreach (var serial in serials)
            {
                if (!named.Add(serial.Trim()))
                {
                    return $"Line {no}: serial number '{serial.Trim()}' is named twice on this note.";
                }
            }

            if (requireCount && product.SerialTracking && product.Type == ProductType.Goods)
            {
                var quantity = line.PrimaryQuantity.Value;
                if (quantity != Math.Truncate(quantity) || quantity != serials.Count)
                {
                    return $"Line {no}: '{product.Name}' is serial-tracked, so returning {quantity:0.####} of it names "
                        + $"{quantity:0.####} serial number(s), and {serials.Count} were given.";
                }
            }
        }

        if (named.Count == 0)
        {
            return null;
        }

        if (debitNote.ReferrerType == DocumentType.PurchaseBill && debitNote.ReferrerId is { } purchaseBillId)
        {
            var received = await ReceivedAsync(db, organizationId, purchaseBillId, cancellationToken);
            var returned = await ReturnedAsync(db, organizationId, purchaseBillId, debitNote.Id, cancellationToken);

            foreach (var (line, no) in lines)
            {
                foreach (var serial in (serialsByLine.GetValueOrDefault(line.Id) ?? []).Select(x => x.Trim()))
                {
                    if (!received.Contains((line.ProductId, serial.ToUpperInvariant())))
                    {
                        return $"Line {no}: serial number '{serial}' was not received on the purchase bill this note "
                            + "returns.";
                    }

                    if (returned.Contains((line.ProductId, serial.ToUpperInvariant())))
                    {
                        return $"Line {no}: serial number '{serial}' has already been returned by another debit note.";
                    }
                }
            }
        }

        // A note returning goods has a warehouse by the time it saves (EnsureDebitNoteWarehouseForGoodsAsync); one that
        // predates phase 43 may not, and Approve refuses it on its own terms before reaching the ledger.
        if (debitNote.WarehouseId is { } warehouseId)
        {
            foreach (var (line, no) in lines)
            {
                var serials = serialsByLine.GetValueOrDefault(line.Id) ?? [];
                if (serials.Count == 0)
                {
                    continue;
                }

                var inStock = await SerialStock.InStockAsync(
                    db, organizationId, line.ProductId, serials, cancellationToken, warehouseId);
                var missing = serials.Select(x => x.Trim()).FirstOrDefault(s =>
                    !inStock.Contains(s, StringComparer.OrdinalIgnoreCase));
                if (missing is not null)
                {
                    return $"Line {no}: serial number '{missing}' is not in stock in this note's warehouse, so it "
                        + "cannot be returned.";
                }
            }
        }

        return null;
    }

    /// <summary>The serials <paramref name="purchaseBillId"/>'s lines received, by product, upper-cased (the
    /// collation's comparison).</summary>
    public static async Task<HashSet<(Guid ProductId, string SerialNo)>> ReceivedAsync(
        IAppDbContext db, Guid organizationId, Guid purchaseBillId, CancellationToken cancellationToken)
    {
        var rows = await (
                from serial in db.DocumentLineSerials
                join line in db.PurchaseBillLines on serial.ParentLineId equals line.Id
                where serial.OrganizationId == organizationId
                    && serial.ParentType == DocumentLineParentType.PurchaseBillLine
                    && line.PurchaseBillId == purchaseBillId
                select new { line.ProductId, serial.SerialNo })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => (x.ProductId, x.SerialNo.ToUpperInvariant()))];
    }

    /// <summary>The serials approved notes of <paramref name="purchaseBillId"/> other than
    /// <paramref name="excludingDebitNoteId"/> sent back, by product, upper-cased. A void gives its serials up.</summary>
    public static async Task<HashSet<(Guid ProductId, string SerialNo)>> ReturnedAsync(
        IAppDbContext db, Guid organizationId, Guid purchaseBillId, Guid? excludingDebitNoteId,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from serial in db.DocumentLineSerials
                join line in db.DebitNoteLines on serial.ParentLineId equals line.Id
                join note in db.DebitNotes on line.DebitNoteId equals note.Id
                where serial.OrganizationId == organizationId
                    && serial.ParentType == DocumentLineParentType.DebitNoteLine
                    && note.OrganizationId == organizationId && note.ReferrerType == DocumentType.PurchaseBill
                    && note.ReferrerId == purchaseBillId && note.Status == DebitNoteStatus.Approved
                    && note.Id != excludingDebitNoteId
                select new { line.ProductId, serial.SerialNo })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => (x.ProductId, x.SerialNo.ToUpperInvariant()))];
    }

    /// <summary>
    /// A draft's save: the serials each line names, positionally, checked (a 400 naming the line; the count waits for
    /// Approve) and written in place of <paramref name="removedLineIds"/>' rows, as the credit note's draft writes its
    /// own.
    /// </summary>
    public static async Task SaveDraftAsync(
        IAppDbContext db,
        DebitNote debitNote,
        IReadOnlyList<IReadOnlyList<string>?> serialsPerLine,
        IReadOnlyCollection<Guid> removedLineIds,
        CancellationToken cancellationToken)
    {
        var lines = debitNote.Lines.ToList();
        var serialsByLine = lines
            .Select((line, i) => (line.Id, Serials: i < serialsPerLine.Count ? serialsPerLine[i] ?? [] : []))
            .Where(x => x.Serials.Count > 0)
            .ToDictionary(x => x.Id, x => x.Serials.Select(s => s.Trim()).Where(s => s.Length > 0).ToList());

        if (await FindProblemAsync(db, debitNote, serialsByLine, requireCount: false, cancellationToken) is { } problem)
        {
            throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("Lines", problem)]);
        }

        await DocumentLineAllocationWriter.ReplaceSerialsAsync(
            db, debitNote.OrganizationId, DocumentLineParentType.DebitNoteLine, removedLineIds,
            [.. serialsByLine.Select(x => (x.Key, (IReadOnlyList<string>?)x.Value))], cancellationToken);
    }

    /// <summary>The serial rows a note's lines name, keyed by line id.</summary>
    public static Task<IReadOnlyDictionary<Guid, List<string>>> LoadAsync(
        IAppDbContext db, DebitNote debitNote, CancellationToken cancellationToken) =>
        LineStockAllocator.LoadSerialsAsync(
            db, debitNote.OrganizationId, DocumentLineParentType.DebitNoteLine,
            [.. debitNote.Lines.Select(x => x.Id)], cancellationToken);
}
