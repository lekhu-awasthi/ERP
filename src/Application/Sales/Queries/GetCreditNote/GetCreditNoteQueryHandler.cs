using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.GetCreditNote;

public sealed class GetCreditNoteQueryHandler(IAppDbContext db) : IRequestHandler<GetCreditNoteQuery, CreditNoteDetailDto>
{
    public async Task<CreditNoteDetailDto> Handle(GetCreditNoteQuery request, CancellationToken cancellationToken)
    {
        var creditNote = await db.CreditNotes
            .Include(x => x.Lines)
            .Include(x => x.Payouts)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Credit note not found.");

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (creditNote.Status == CreditNoteStatus.Approved)
        {
            // Phase 37 -- every entry this document posted, not "the" entry (phase 36's
            // finding, generalised): a cost catch-up rides on the same
            // (SourceDocumentType, SourceDocumentId) pair, and SingleOrDefaultAsync throws on
            // two rows exactly as SingleAsync does. The panel shows what the document really
            // did to the ledger, so it shows all of it.
            var glEntries = await SourceDocumentGlEntries.LoadAsync(
                db, DocumentType.CreditNote, creditNote.Id, cancellationToken);

            glLines = glEntries.Count == 0
                ? null
                : glEntries.SelectMany(e => e.Lines)
                    .Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        // Phase 52 -- the unit each line names, read back through the one shared reader so the
        // eight detail queries cannot drift in how they answer the same question.
        var unitNames = await DocumentLineUnitResolver.LoadUnitNamesAsync(
            db, request.OrganizationId, creditNote.Lines.Select(x => x.UnitId), cancellationToken);

        return new CreditNoteDetailDto(
            creditNote.Id,
            creditNote.OrganizationId,
            creditNote.ContactId,
            creditNote.Code,
            creditNote.Date,
            creditNote.Reference,
            creditNote.Status,
            creditNote.ApprovedByUserId,
            creditNote.ApprovedAt,
            creditNote.CreatedAt,
            creditNote.ReferrerType,
            creditNote.ReferrerId,
            creditNote.DiscountPct,
            creditNote.Terms,
            creditNote.Lines.Select(x => new CreditNoteLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount,
                x.UnitId, x.UnitId is null ? null : unitNames.GetValueOrDefault(x.UnitId.Value), x.ConversionFactor)
            {
                ServiceChargeAmount = x.ServiceChargeAmount,
                PackagingFeeAmount = x.PackagingFeeAmount,
                DiscountPerUnit = x.DiscountPerUnit,
                BillDiscountAmount = x.BillDiscountAmount,
            }).ToList(),
            glLines,
            creditNote.CurrencyCode,
            creditNote.ExchangeRate,
            creditNote.LocationId,
            await ReadPosRefundAsync(creditNote, cancellationToken),
            await db.CreditNotePrints.CountAsync(
                x => x.OrganizationId == request.OrganizationId && x.CreditNoteId == creditNote.Id, cancellationToken))
        {
            AgainstInvoiceId = creditNote.AgainstInvoiceId,
            AgainstInvoiceNumber = creditNote.AgainstInvoiceNumber,
            AgainstInvoiceDate = creditNote.AgainstInvoiceDate,
            Reason = creditNote.Reason,
            RelatedInvoice = await ReadRelatedInvoiceAsync(creditNote, cancellationToken),
        };
    }

    private async Task<RelatedInvoiceDto?> ReadRelatedInvoiceAsync(CreditNote creditNote, CancellationToken cancellationToken)
    {
        if (creditNote.RelatedInvoiceId is not { } invoiceId)
        {
            return null;
        }

        var invoice = await db.Invoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganizationId == creditNote.OrganizationId, cancellationToken);

        return invoice is null
            ? null
            : new RelatedInvoiceDto(invoice.Id, invoice.Code, invoice.Date, invoice.GrandTotal, invoice.CurrencyCode);
    }

    private async Task<CreditNotePosRefundDto?> ReadPosRefundAsync(CreditNote creditNote, CancellationToken cancellationToken)
    {
        if (creditNote.Channel != SalesChannel.Pos)
        {
            return null;
        }

        var sessionCode = await db.PosSessions
            .Where(x => x.Id == creditNote.PosSessionId && x.OrganizationId == creditNote.OrganizationId)
            .Select(x => x.Code)
            .SingleOrDefaultAsync(cancellationToken);

        var modeIds = creditNote.Payouts.Select(x => x.PaymentModeId).Distinct().ToList();
        var modeNames = await db.PaymentModes
            .Where(x => x.OrganizationId == creditNote.OrganizationId && modeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return new CreditNotePosRefundDto(
            creditNote.PosSessionId,
            sessionCode,
            creditNote.Reason,
            creditNote.ServiceChargeTotal,
            creditNote.PackagingFeeTotal,
            creditNote.RoundOff,
            creditNote.GrandTotal,
            [.. creditNote.Payouts.Select(x => new CreditNotePayoutDto(
                x.PaymentModeId, modeNames.GetValueOrDefault(x.PaymentModeId, ""), x.Kind, x.Amount))],
            creditNote.PaidOutAmount,
            creditNote.ToAccountAmount)
        {
            WarehouseName = await ReadReturnWarehouseNameAsync(creditNote, cancellationToken),
        };
    }

    /// <summary>Phase 85 -- where a till refund's goods went back into stock: the rule
    /// <c>CreditNoteApprovalPosting</c> restocked by (the note's warehouse, else the sale's), asked only when a line is
    /// Goods.</summary>
    private async Task<string?> ReadReturnWarehouseNameAsync(CreditNote creditNote, CancellationToken cancellationToken)
    {
        var productIds = creditNote.Lines.Select(x => x.ProductId).Distinct().ToList();
        var anyGoods = await db.Products.AnyAsync(
            x => x.OrganizationId == creditNote.OrganizationId && productIds.Contains(x.Id) && x.Type == ProductType.Goods,
            cancellationToken);
        if (!anyGoods)
        {
            return null;
        }

        var warehouseId = creditNote.WarehouseId;
        if (warehouseId is null && creditNote.ReferrerType == DocumentType.Invoice && creditNote.ReferrerId is { } invoiceId)
        {
            warehouseId = await db.Invoices
                .Where(x => x.Id == invoiceId && x.OrganizationId == creditNote.OrganizationId)
                .Select(x => (Guid?)x.WarehouseId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return warehouseId is { } id
            ? await db.Warehouses
                .Where(x => x.Id == id && x.OrganizationId == creditNote.OrganizationId)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
    }
}
