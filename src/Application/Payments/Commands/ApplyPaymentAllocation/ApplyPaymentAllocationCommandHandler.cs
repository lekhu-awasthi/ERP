using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Commands.ApplyPaymentAllocation;

public sealed class ApplyPaymentAllocationCommandHandler(IAppDbContext db)
    : IRequestHandler<ApplyPaymentAllocationCommand, ApplyPaymentAllocationResult>
{
    public async Task<ApplyPaymentAllocationResult> Handle(
        ApplyPaymentAllocationCommand request, CancellationToken cancellationToken)
    {
        await PaymentValidation.EnsureAllocationTargetsExistAsync(
            db, request.OrganizationId,
            [new PaymentAllocationInput(request.TargetDocumentType, request.TargetDocumentId, request.Amount)],
            cancellationToken);

        return request.SourceType == DocumentType.Payment
            ? await ApplyToPaymentAsync(request, cancellationToken)
            : await ApplyToJournalVoucherLineAsync(request, cancellationToken);
    }

    private async Task<ApplyPaymentAllocationResult> ApplyToPaymentAsync(
        ApplyPaymentAllocationCommand request, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .SingleOrDefaultAsync(x => x.Id == request.SourceId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        if (payment.Status != PaymentStatus.Approved)
        {
            throw new ConflictException("Only an Approved payment can be allocated further.");
        }

        var existingAllocations = await db.PaymentAllocations
            .Where(x => x.SourceType == DocumentType.Payment && x.SourceId == payment.Id)
            .ToListAsync(cancellationToken);
        payment.AttachAllocations(existingAllocations);

        if (payment.Allocations.Sum(x => x.Amount) + request.Amount > payment.Amount)
        {
            throw new ConflictException("A payment's allocations cannot exceed its Amount.");
        }

        payment.AllocateFurther(request.TargetDocumentType, request.TargetDocumentId, request.Amount);
        db.PaymentAllocations.Add(payment.Allocations[^1]);

        await db.SaveChangesAsync(cancellationToken);

        var allocated = payment.Allocations.Sum(x => x.Amount);
        return new ApplyPaymentAllocationResult(payment.Id, payment.Amount, allocated, payment.Amount - allocated);
    }

    /// <summary>
    /// Decision #2 -- a JournalVoucherLine has no aggregate-root behavior of its own (see the class
    /// doc comment), so this handler enforces the "doesn't exceed the line's own available amount"
    /// invariant directly rather than routing through a domain method. Received (Customer, AR)
    /// credits sit on the Credit side of the line; Paid (Supplier, AP) credits sit on the Debit
    /// side -- mirrors PaymentValidation.EnsureContactExistsAsync's own Direction&lt;-&gt;ContactType
    /// mapping. Not live-confirmed against Tigg (same "safe default, not live-confirmed" caveat as
    /// decision #4) -- flagged for future live verification.
    /// </summary>
    private async Task<ApplyPaymentAllocationResult> ApplyToJournalVoucherLineAsync(
        ApplyPaymentAllocationCommand request, CancellationToken cancellationToken)
    {
        var line = await db.JournalVoucherLines
            .SingleOrDefaultAsync(x => x.Id == request.SourceId, cancellationToken)
            ?? throw new NotFoundException("Journal voucher line not found.");

        if (line.ContactId is not { } contactId)
        {
            throw new ConflictException("This journal voucher line has no Contact and cannot be allocated.");
        }

        var journalVoucher = await db.JournalVouchers.SingleOrDefaultAsync(
            x => x.Id == line.JournalVoucherId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Journal voucher not found.");

        if (journalVoucher.Status != JournalVoucherStatus.Approved)
        {
            throw new ConflictException("Only an Approved journal voucher's lines can be allocated.");
        }

        var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == contactId, cancellationToken)
            ?? throw new NotFoundException("Contact not found.");

        var lineCreditAmount = contact.Type == ContactType.Customer ? line.Credit : line.Debit;
        if (lineCreditAmount <= 0)
        {
            throw new ConflictException("This journal voucher line has no available balance for this contact's direction.");
        }

        var existingAllocated = await db.PaymentAllocations
            .Where(x => x.SourceType == DocumentType.JournalVoucher && x.SourceId == line.Id)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        if (existingAllocated + request.Amount > lineCreditAmount)
        {
            throw new ConflictException("A journal voucher line's allocations cannot exceed its own Debit/Credit amount.");
        }

        var allocation = PaymentAllocation.Create(
            DocumentType.JournalVoucher, line.Id, request.TargetDocumentType, request.TargetDocumentId, request.Amount);
        db.PaymentAllocations.Add(allocation);

        await db.SaveChangesAsync(cancellationToken);

        var allocated = existingAllocated + request.Amount;
        return new ApplyPaymentAllocationResult(line.Id, lineCreditAmount, allocated, lineCreditAmount - allocated);
    }
}
