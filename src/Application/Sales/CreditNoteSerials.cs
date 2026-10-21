using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales;

/// <summary>
/// Phase 91 -- which serials a credit note may bring back. Phase 51 said a returned serial "re-enters stock as a new
/// layer carrying the same SerialNo" and wrote nothing that carried it: <c>CreditNoteApprovalPosting</c> restocked a
/// serial-tracked product as one layer of the whole quantity with no serial, so the phone came back as stock no serial
/// could ever be sold from again. The vendor carries <c>serial_numbers</c> on the note's line and shows the unit
/// AVAILABLE once refunded (read live, phase-91-status.md § 0), which is the rule here:
/// <list type="bullet">
/// <item>a serial may be named only on a serial-tracked product's line, once on the whole note;</item>
/// <item>only a note that returns goods (converted from its invoice: <see cref="CreditNote.IsConversionFromInvoice"/>)
/// names any, because only that note restocks;</item>
/// <item>each must be one the invoice sold of that product, and not one an approved note of the same invoice already
/// brought back (a void gives it up, by phase 65's rule: a sum over the documents not voided, never a counter);</item>
/// <item>at Approve, a serial-tracked Goods line names exactly its primary quantity of them -- a draft may still be
/// filling them in, as the invoice's own draft may.</item>
/// </list>
/// The callers throw the error their door owes: a 400 on a draft's save, a 409 at Approve.
/// </summary>
internal static class CreditNoteSerials
{
    /// <summary>The first problem with the serials <paramref name="serialsByLine"/> names on
    /// <paramref name="creditNote"/>'s lines, with the line number; null when there is none.</summary>
    public static async Task<string?> FindProblemAsync(
        IAppDbContext db,
        CreditNote creditNote,
        IReadOnlyDictionary<Guid, List<string>> serialsByLine,
        bool requireCount,
        CancellationToken cancellationToken)
    {
        var organizationId = creditNote.OrganizationId;
        var productIds = creditNote.Lines.Select(x => x.ProductId).Distinct().ToList();
        // Phase 95 -- serial-tracked as the company's switch lets it be: off overrides the product's own.
        var serialOn = (await StockTrackingSwitches.LoadAsync(db, organizationId, cancellationToken)).Serial;
        var products = await db.Products
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Type, SerialTracking = serialOn && x.SerialTracking })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var returnsGoods = creditNote.IsConversionFromInvoice;
        var lines = creditNote.Lines.Select((line, i) => (Line: line, No: i + 1)).ToList();
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

            if (serials.Count > 0 && !returnsGoods)
            {
                return $"Line {no}: this note returns no goods (it is not converted from its invoice), so it names no "
                    + "serial numbers.";
            }

            foreach (var serial in serials)
            {
                if (!named.Add(serial.Trim()))
                {
                    return $"Line {no}: serial number '{serial.Trim()}' is named twice on this note.";
                }
            }

            if (requireCount && returnsGoods && product.SerialTracking && product.Type == ProductType.Goods)
            {
                var quantity = line.PrimaryQuantity.Value;
                if (quantity != Math.Truncate(quantity) || quantity != serials.Count)
                {
                    return $"Line {no}: '{product.Name}' is serial-tracked, so returning {quantity:0.####} of it names "
                        + $"{quantity:0.####} serial number(s), and {serials.Count} were given.";
                }
            }
        }

        if (!returnsGoods || named.Count == 0)
        {
            return null;
        }

        var invoiceId = creditNote.ReferrerId!.Value;
        var sold = await SoldAsync(db, organizationId, invoiceId, cancellationToken);
        var returned = await ReturnedAsync(db, organizationId, invoiceId, creditNote.Id, cancellationToken);

        foreach (var (line, no) in lines)
        {
            foreach (var serial in (serialsByLine.GetValueOrDefault(line.Id) ?? []).Select(x => x.Trim()))
            {
                if (!sold.Contains((line.ProductId, serial)))
                {
                    return $"Line {no}: serial number '{serial}' was not sold on the invoice this note returns.";
                }

                if (returned.Contains((line.ProductId, serial)))
                {
                    return $"Line {no}: serial number '{serial}' has already been returned by another credit note.";
                }
            }
        }

        return null;
    }

    /// <summary>The serials <paramref name="invoiceId"/>'s lines sold, by product.</summary>
    public static async Task<HashSet<(Guid ProductId, string SerialNo)>> SoldAsync(
        IAppDbContext db, Guid organizationId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var rows = await (
                from serial in db.DocumentLineSerials
                join line in db.InvoiceLines on serial.ParentLineId equals line.Id
                where serial.OrganizationId == organizationId && serial.ParentType == DocumentLineParentType.InvoiceLine
                    && line.InvoiceId == invoiceId
                select new { line.ProductId, serial.SerialNo })
            .ToListAsync(cancellationToken);

        return rows.Select(x => (x.ProductId, x.SerialNo)).ToHashSet(SerialKeyComparer.Instance);
    }

    /// <summary>The serials approved notes of <paramref name="invoiceId"/> other than <paramref name="excludingCreditNoteId"/>
    /// brought back, by product. A void gives its serials up.</summary>
    public static async Task<HashSet<(Guid ProductId, string SerialNo)>> ReturnedAsync(
        IAppDbContext db, Guid organizationId, Guid invoiceId, Guid? excludingCreditNoteId, CancellationToken cancellationToken)
    {
        var rows = await (
                from serial in db.DocumentLineSerials
                join line in db.CreditNoteLines on serial.ParentLineId equals line.Id
                join note in db.CreditNotes on line.CreditNoteId equals note.Id
                where serial.OrganizationId == organizationId && serial.ParentType == DocumentLineParentType.CreditNoteLine
                    && note.OrganizationId == organizationId && note.ReferrerType == Domain.Common.DocumentType.Invoice
                    && note.ReferrerId == invoiceId && note.Status == CreditNoteStatus.Approved
                    && note.Id != excludingCreditNoteId
                select new { line.ProductId, serial.SerialNo })
            .ToListAsync(cancellationToken);

        return rows.Select(x => (x.ProductId, x.SerialNo)).ToHashSet(SerialKeyComparer.Instance);
    }

    /// <summary>
    /// An ERP draft's save: the serials each line names, positionally, checked (a 400 naming the line; the count waits
    /// for Approve) and written in place of <paramref name="removedLineIds"/>' rows, as the invoice's draft writes its own.
    /// </summary>
    public static async Task SaveDraftAsync(
        IAppDbContext db,
        CreditNote creditNote,
        IReadOnlyList<IReadOnlyList<string>?> serialsPerLine,
        IReadOnlyCollection<Guid> removedLineIds,
        CancellationToken cancellationToken)
    {
        var lines = creditNote.Lines.ToList();
        var serialsByLine = lines
            .Select((line, i) => (line.Id, Serials: i < serialsPerLine.Count ? serialsPerLine[i] ?? [] : []))
            .Where(x => x.Serials.Count > 0)
            .ToDictionary(x => x.Id, x => x.Serials.Select(s => s.Trim()).ToList());

        if (await FindProblemAsync(db, creditNote, serialsByLine, requireCount: false, cancellationToken) is { } problem)
        {
            throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("Lines", problem)]);
        }

        await DocumentLineAllocationWriter.ReplaceSerialsAsync(
            db, creditNote.OrganizationId, DocumentLineParentType.CreditNoteLine, removedLineIds,
            [.. serialsByLine.Select(x => (x.Key, (IReadOnlyList<string>?)x.Value))], cancellationToken);
    }

    /// <summary>The serial rows a note's lines name, keyed by line id.</summary>
    public static Task<IReadOnlyDictionary<Guid, List<string>>> LoadAsync(
        IAppDbContext db, CreditNote creditNote, CancellationToken cancellationToken) =>
        LineStockAllocator.LoadSerialsAsync(
            db, creditNote.OrganizationId, DocumentLineParentType.CreditNoteLine,
            [.. creditNote.Lines.Select(x => x.Id)], cancellationToken);

    /// <summary>Serials compare as the database does: case-insensitively (phase 34b), and trimmed as they are stored.</summary>
    private sealed class SerialKeyComparer : IEqualityComparer<(Guid ProductId, string SerialNo)>
    {
        public static readonly SerialKeyComparer Instance = new();

        public bool Equals((Guid ProductId, string SerialNo) x, (Guid ProductId, string SerialNo) y) =>
            x.ProductId == y.ProductId && StringComparer.OrdinalIgnoreCase.Equals(x.SerialNo, y.SerialNo);

        public int GetHashCode((Guid ProductId, string SerialNo) obj) =>
            HashCode.Combine(obj.ProductId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SerialNo));
    }
}
