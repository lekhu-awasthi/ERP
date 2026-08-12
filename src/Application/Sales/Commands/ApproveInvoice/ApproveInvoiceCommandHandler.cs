using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.ApproveInvoice;

public sealed class ApproveInvoiceCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<InvoicePostingInput> postingRule,
    IStockAvailabilityPolicy stockAvailabilityPolicy)
    : IRequestHandler<ApproveInvoiceCommand, ApproveInvoiceResult>
{
    public async Task<ApproveInvoiceResult> Handle(ApproveInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");

        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new ConflictException("Only a Draft invoice can be approved.");
        }

        if (invoice.Lines.Count == 0)
        {
            throw new ConflictException("An invoice needs at least one line to be approved.");
        }

        // Warn/Reject are both no-ops today (AlwaysOkStockAvailabilityPolicy) -- the branch exists
        // so a real Phase 7 policy plugs in without touching this handler.
        if (stockAvailabilityPolicy.Check(invoice) == StockAvailabilityStatus.Reject)
        {
            throw new ConflictException("Insufficient stock to approve this invoice.");
        }

        var postingInput = await InvoiceAccountResolver.ResolveAsync(
            db, request.OrganizationId, invoice.Lines.Select(x => (x.ProductId, x.Amount, x.VatAmount)), cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.Invoice, cancellationToken);

        invoice.Approve(currentUser.UserId, code);

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.Invoice, invoice.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveInvoiceResult(invoice.Id, invoice.Code, invoice.Status, invoice.ApprovedAt);
    }
}
