using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Payments.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Commands.ApprovePayment;

public sealed class ApprovePaymentCommandHandler(
    IAppDbContext db, IDocumentNumberGenerator numberGenerator, ICurrentUserService currentUser,
    IGlPostingRule<PaymentPostingInput> postingRule)
    : IRequestHandler<ApprovePaymentCommand, ApprovePaymentResult>
{
    public async Task<ApprovePaymentResult> Handle(ApprovePaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        if (payment.Status != PaymentStatus.Draft)
        {
            throw new ConflictException("Only a Draft payment can be approved.");
        }

        if (payment.Allocations.Count == 0 || payment.Allocations.Sum(x => x.Amount) != payment.Amount)
        {
            throw new ConflictException("A payment's allocations must add up to exactly its Amount to be approved.");
        }

        // Phase 16a: EnsureAllocationTargetsExistAsync only ran at Create/Update time -- a target
        // Invoice/PurchaseBill that was Approved when this Payment was drafted can be voided
        // afterward, and nothing re-checked that before this fix. Re-validate right before
        // actually posting against it, not just when the allocation was first typed in.
        var invoiceIds = payment.Allocations.Where(x => x.TargetDocumentType == DocumentType.Invoice)
            .Select(x => x.TargetDocumentId).Distinct().ToList();
        if (invoiceIds.Count > 0)
        {
            var approvedCount = await db.Invoices.CountAsync(
                x => invoiceIds.Contains(x.Id) && x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved,
                cancellationToken);
            if (approvedCount != invoiceIds.Count)
            {
                throw new ConflictException(
                    "One or more allocation target invoices are no longer Approved (voided since this payment was drafted).");
            }
        }

        var purchaseBillIds = payment.Allocations.Where(x => x.TargetDocumentType == DocumentType.PurchaseBill)
            .Select(x => x.TargetDocumentId).Distinct().ToList();
        if (purchaseBillIds.Count > 0)
        {
            var approvedCount = await db.PurchaseBills.CountAsync(
                x => purchaseBillIds.Contains(x.Id) && x.OrganizationId == request.OrganizationId
                    && x.Status == PurchaseBillStatus.Approved,
                cancellationToken);
            if (approvedCount != purchaseBillIds.Count)
            {
                throw new ConflictException(
                    "One or more allocation target purchase bills are no longer Approved (voided since this payment was drafted).");
            }
        }

        var postingInput = await PaymentAccountResolver.ResolveAsync(
            db, request.OrganizationId, payment.AccountId, payment.Amount, payment.Direction, cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.Payment, cancellationToken);

        payment.Approve(currentUser.UserId, code);

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.Payment, payment.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApprovePaymentResult(payment.Id, payment.Code, payment.Status, payment.ApprovedAt);
    }
}
