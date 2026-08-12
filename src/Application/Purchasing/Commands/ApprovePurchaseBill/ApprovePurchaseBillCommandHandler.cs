using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;

/// <summary>
/// Approve() increments stock rather than decrements (Invoice's side) -- confirmed live no
/// Negative Stock Balance dialog on PurchaseBill approval (erp-module-scan.md's hands-on pass item
/// 10), so there's no availability policy to check here the way ApproveInvoiceCommandHandler
/// checks IStockAvailabilityPolicy. The actual FIFO-layer increment is still entirely absent
/// (Invoice's own decrement is equally stubbed) -- both are Phase 7's job.
/// </summary>
public sealed class ApprovePurchaseBillCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<PurchaseBillPostingInput> postingRule)
    : IRequestHandler<ApprovePurchaseBillCommand, ApprovePurchaseBillResult>
{
    public async Task<ApprovePurchaseBillResult> Handle(ApprovePurchaseBillCommand request, CancellationToken cancellationToken)
    {
        var purchaseBill = await db.PurchaseBills
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase bill not found.");

        if (purchaseBill.Status != PurchaseBillStatus.Draft)
        {
            throw new ConflictException("Only a Draft purchase bill can be approved.");
        }

        if (purchaseBill.Lines.Count == 0)
        {
            throw new ConflictException("A purchase bill needs at least one line to be approved.");
        }

        var postingInput = await PurchaseBillAccountResolver.ResolveAsync(
            db, request.OrganizationId, purchaseBill.Lines.Select(x => (x.ProductId, x.Amount, x.VatAmount)),
            purchaseBill.TdsAmount, cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.PurchaseBill, cancellationToken);

        purchaseBill.Approve(currentUser.UserId, code);

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.PurchaseBill, purchaseBill.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApprovePurchaseBillResult(purchaseBill.Id, purchaseBill.Code, purchaseBill.Status, purchaseBill.ApprovedAt);
    }
}
