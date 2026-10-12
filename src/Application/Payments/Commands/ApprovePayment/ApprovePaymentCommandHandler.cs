using ErpApp.Application.Accounting.Cash;
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
    IGlPostingRule<PaymentPostingInput> postingRule,
    ICashBalancePolicy cashBalancePolicy)
    : IRequestHandler<ApprovePaymentCommand, ApprovePaymentResult>
{
    public async Task<ApprovePaymentResult> Handle(ApprovePaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        if (payment.Status != PaymentStatus.Draft)
        {
            throw new ConflictException("Only a Draft payment can be approved.");
        }

        var existingAllocations = await db.PaymentAllocations
            .Where(x => x.SourceType == DocumentType.Payment && x.SourceId == payment.Id)
            .ToListAsync(cancellationToken);
        payment.AttachAllocations(existingAllocations);

        if (payment.Allocations.Sum(x => x.Amount) > payment.Amount)
        {
            throw new ConflictException("A payment's allocations cannot exceed its Amount.");
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

        // Phase 88 -- the entry is built by the one builder the form's preview also calls (PaymentGlLines): the amount
        // converted at the payment's rate, the realised forex from the targets' own rates (phase 28), and each allocated
        // part settled out of the control account its target recorded (phase 87).
        var plan = await PaymentGlLines.BuildAsync(
            db, postingRule, request.OrganizationId, PaymentGlDraft.Of(payment), cancellationToken);

        // Phase 31 -- NegativeCashBalanceAction. Only a Paid payment takes money out; a Received
        // one debits the account and can never overdraw it. Checked in base currency, because that
        // is what GlLine holds and therefore what the account's balance is denominated in.
        if (payment.Direction == PaymentDirection.Paid)
        {
            var cashStatus = await cashBalancePolicy.CheckAsync(
                request.OrganizationId,
                [new CashOutflow(payment.AccountId, ExchangeRates.ToBase(payment.Amount, payment.ExchangeRate))],
                cancellationToken);

            CashBalanceGuard.Enforce(cashStatus, request.OverrideNegativeCashBalanceWarning, "payment");
        }

        var code = await numberGenerator.GetNextNumberAsync(
            request.OrganizationId, DocumentType.Payment, cancellationToken, payment.LocationId);

        // Phase 87 -- where the unallocated part goes (today's default), kept for a later allocation.
        payment.RecordUnallocatedControlAccount(plan.UnallocatedControlAccountId);

        payment.Approve(currentUser.UserId, code);

        var glEntry = GlJournalEntry.Post(
            request.OrganizationId, DocumentType.Payment, payment.Id, plan.Lines, payment.LocationId);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApprovePaymentResult(payment.Id, payment.Code, payment.Status, payment.ApprovedAt);
    }
}
