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

        // Phase 28: the payment's own Amount is in its transaction currency, so it converts here
        // like every other document's line amounts (see ExchangeRates). The realised exchange
        // difference its allocations produce is a separate, second leg -- computed below, from the
        // rates the *target* documents were booked at.
        var postingInput = await PaymentAccountResolver.ResolveAsync(
            db, request.OrganizationId, payment.AccountId,
            ExchangeRates.ToBase(payment.Amount, payment.ExchangeRate), payment.Direction, cancellationToken);

        var forex = await PaymentForexCalculator.CalculateAsync(
            db, request.OrganizationId, payment.Direction, payment.CurrencyCode, payment.ExchangeRate,
            await LoadForexAllocationsAsync(request.OrganizationId, payment.Allocations, cancellationToken),
            cancellationToken);

        if (forex is not null)
        {
            postingInput = postingInput with { Forex = forex };
        }

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.Payment, cancellationToken);

        payment.Approve(currentUser.UserId, code);

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.Payment, payment.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApprovePaymentResult(payment.Id, payment.Code, payment.Status, payment.ApprovedAt);
    }

    /// <summary>
    /// Reads each allocation target's own CurrencyCode/ExchangeRate -- the rate the receivable or
    /// payable being settled was actually booked into the general ledger at. Batched per document
    /// type rather than per row, the same shape <c>GlSourceDocumentResolver</c> uses for the eleven
    /// GL-posting types (phase 26a). Only Invoice and PurchaseBill can be allocation targets, so
    /// there are exactly two queries and both are skipped when there are no allocations of that
    /// type.
    /// </summary>
    private async Task<IReadOnlyList<ForexAllocation>> LoadForexAllocationsAsync(
        Guid organizationId, IReadOnlyList<PaymentAllocation> allocations, CancellationToken cancellationToken)
    {
        if (allocations.Count == 0)
        {
            return [];
        }

        var invoiceIds = allocations.Where(x => x.TargetDocumentType == DocumentType.Invoice)
            .Select(x => x.TargetDocumentId).Distinct().ToList();
        var billIds = allocations.Where(x => x.TargetDocumentType == DocumentType.PurchaseBill)
            .Select(x => x.TargetDocumentId).Distinct().ToList();

        var invoiceRates = invoiceIds.Count == 0
            ? []
            : await db.Invoices
                .Where(x => x.OrganizationId == organizationId && invoiceIds.Contains(x.Id))
                .Select(x => new { x.Id, x.CurrencyCode, x.ExchangeRate })
                .ToDictionaryAsync(x => x.Id, x => (x.CurrencyCode, x.ExchangeRate), cancellationToken);

        var billRates = billIds.Count == 0
            ? []
            : await db.PurchaseBills
                .Where(x => x.OrganizationId == organizationId && billIds.Contains(x.Id))
                .Select(x => new { x.Id, x.CurrencyCode, x.ExchangeRate })
                .ToDictionaryAsync(x => x.Id, x => (x.CurrencyCode, x.ExchangeRate), cancellationToken);

        var result = new List<ForexAllocation>();
        foreach (var allocation in allocations)
        {
            var target = allocation.TargetDocumentType == DocumentType.Invoice
                ? invoiceRates.GetValueOrDefault(allocation.TargetDocumentId)
                : billRates.GetValueOrDefault(allocation.TargetDocumentId);

            // A target this handler could not read is one the checks above already rejected, so a
            // missing entry can only be a target type that carries no currency at all. Treating it
            // as base currency at rate 1 keeps the arithmetic total rather than throwing on a
            // shape that cannot reach here today.
            result.Add(new ForexAllocation(
                allocation.Amount,
                target.CurrencyCode ?? CurrencyCatalog.BaseCode,
                target.ExchangeRate == 0 ? ExchangeRates.BaseRate : target.ExchangeRate));
        }

        return result;
    }
}
