using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.VoidInvoice;

/// <summary>
/// Guards (roadmap Phase 16a): rejects (409) if a non-Void CreditNote still references this
/// Invoice, or an Approved Payment still allocates against it -- both dependents must be voided
/// first, in reverse order. Stock: every Goods line that actually consumed FIFO stock at Approve
/// time carries its own InvoiceLine.CogsUnitCost (Phase 7) -- restocked via IncrementAsync at that
/// exact recorded cost, the same "put it back at what it left at" precedent
/// ApproveCreditNoteCommandHandler's Invoice-reversal already established, just triggered by
/// voiding the Invoice itself rather than a CreditNote against it. GL: a mirror-image reversal of
/// the Approve-time entry (Debit Sales Revenue/VAT Payable/COGS, Credit AR/Inventory).
/// </summary>
public sealed class VoidInvoiceCommandHandler(IAppDbContext db, ICurrentUserService currentUser, IStockLedgerService stockLedgerService)
    : IRequestHandler<VoidInvoiceCommand, VoidInvoiceResult>
{
    public async Task<VoidInvoiceResult> Handle(VoidInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");

        if (invoice.Status != InvoiceStatus.Approved)
        {
            throw new ConflictException("Only an Approved invoice can be voided.");
        }

        var hasNonVoidCreditNote = await db.CreditNotes.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.ReferrerType == DocumentType.Invoice
                && x.ReferrerId == invoice.Id && x.Status != CreditNoteStatus.Void,
            cancellationToken);
        if (hasNonVoidCreditNote)
        {
            throw new ConflictException("Cannot void this invoice -- void the credit note(s) issued against it first.");
        }

        // Phase 17 decision #2: scoped to Payment-sourced allocations only -- see the matching note
        // in ContactAgeingSummaryQueryHandler. A JournalVoucher-sourced allocation against this
        // invoice wouldn't block a Void here yet (docs/phase-17-status.md's Known limitation).
        var hasApprovedPaymentAllocation = await (
            from a in db.PaymentAllocations
            where a.SourceType == DocumentType.Payment
            join p in db.Payments on a.SourceId equals p.Id
            where a.TargetDocumentType == DocumentType.Invoice && a.TargetDocumentId == invoice.Id
                  && p.Status == PaymentStatus.Approved
            select a.Id).AnyAsync(cancellationToken);
        if (hasApprovedPaymentAllocation)
        {
            throw new ConflictException("Cannot void this invoice -- void the payment(s) allocated against it first.");
        }

        var originalEntry = await db.GlJournalEntries
            .Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.Invoice && x.SourceDocumentId == invoice.Id, cancellationToken);

        invoice.Void(currentUser.UserId);

        foreach (var line in invoice.Lines.Where(x => x.CogsUnitCost is not null))
        {
            await stockLedgerService.IncrementAsync(
                request.OrganizationId, line.ProductId, invoice.WarehouseId, line.Quantity, line.CogsUnitCost!.Value,
                DocumentType.Invoice, invoice.Id, invoice.Date, cancellationToken);
        }

        db.GlJournalEntries.Add(GlJournalEntry.PostReversalOf(originalEntry));

        await db.SaveChangesAsync(cancellationToken);

        return new VoidInvoiceResult(invoice.Id, invoice.Code, invoice.Status, invoice.VoidedAt);
    }
}
