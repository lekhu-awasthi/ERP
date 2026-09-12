using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Payments.Posting;
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

        // Before anything is written: a cross-currency allocation is refused outright (phase 28
        // Decision F), and a same-currency one at a different rate produces the realised difference
        // this path used to drop on the floor.
        var forexLines = await BuildRealisedForexLinesAsync(
            request, payment.Direction, payment.CurrencyCode, payment.ExchangeRate, cancellationToken);

        payment.AllocateFurther(request.TargetDocumentType, request.TargetDocumentId, request.Amount);
        db.PaymentAllocations.Add(payment.Allocations[^1]);

        if (forexLines.Count > 0)
        {
            db.GlJournalEntries.Add(GlJournalEntry.Post(
                request.OrganizationId, DocumentType.Payment, payment.Id, forexLines, payment.LocationId));
        }

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

        // Phase 36 -- the same realised difference the Payment branch above books. A voucher line's
        // Debit/Credit are in the *voucher's* currency (ApproveJournalVoucherCommandHandler converts
        // them at its rate), so settling an invoice booked at another rate leaves exactly the same
        // residue on the control account, and the direction follows the contact's type the way it
        // follows PaymentDirection there.
        var direction = contact.Type == ContactType.Customer ? PaymentDirection.Received : PaymentDirection.Paid;
        var forexLines = await BuildRealisedForexLinesAsync(
            request, direction, journalVoucher.CurrencyCode, journalVoucher.ExchangeRate, cancellationToken);

        var allocation = PaymentAllocation.Create(
            DocumentType.JournalVoucher, line.Id, request.TargetDocumentType, request.TargetDocumentId, request.Amount);
        db.PaymentAllocations.Add(allocation);

        if (forexLines.Count > 0)
        {
            // Against the *voucher*, not the line: a line is not a GL source document, and
            // VoidJournalVoucherCommandHandler reverses everything posted against the voucher's id.
            db.GlJournalEntries.Add(GlJournalEntry.Post(
                request.OrganizationId, DocumentType.JournalVoucher, journalVoucher.Id, forexLines,
                journalVoucher.LocationId));
        }

        await db.SaveChangesAsync(cancellationToken);

        var allocated = existingAllocated + request.Amount;
        return new ApplyPaymentAllocationResult(line.Id, lineCreditAmount, allocated, lineCreditAmount - allocated);
    }

    /// <summary>
    /// Phase 36 -- the realised exchange difference <b>this one allocation</b> produces, as the GL
    /// lines that book it, or an empty list when there is none.
    ///
    /// <para><b>Why this path needed it at all.</b> Approve-time allocation has posted the forex leg
    /// since phase 28, netted across the payment's allocations and appended to its own entry. This
    /// path -- phase 17's Allocate screens, applying more of an already-Approved credit -- added a
    /// <c>PaymentAllocation</c> row and touched the ledger not at all, so the same two documents
    /// settled at the same two rates produced a forex figure through one door and nothing through
    /// the other. The control account then kept a residue no later document would ever clear, which
    /// is the exact failure phase 28 wrote <see cref="PaymentForexCalculator"/> to prevent.</para>
    ///
    /// <para><b>This allocation only, and its own entry.</b> The netting <c>CalculateAsync</c> does
    /// is netting across the allocations being posted <i>now</i>: every earlier allocation of this
    /// source was already booked, at Approve time or by an earlier call here, so re-netting them
    /// would post their differences a second time. The source document is already Approved and its
    /// entry already written, and a posted <c>GlJournalEntry</c> is append-only in this codebase
    /// (phase 16a) -- so the correction is a second entry against the same source document, which
    /// is well-formed only because the forex pair balances on its own
    /// (<see cref="PaymentPostingRule.ForexLines"/>). Everything that reads a document's entries
    /// now reads all of them; see <see cref="SourceDocumentGlEntries"/>.</para>
    /// </summary>
    private async Task<IReadOnlyList<GlLineInput>> BuildRealisedForexLinesAsync(
        ApplyPaymentAllocationCommand request,
        PaymentDirection direction,
        string sourceCurrencyCode,
        decimal sourceExchangeRate,
        CancellationToken cancellationToken)
    {
        var targets = await AllocationTargetRates.LoadAsync(
            db, request.OrganizationId,
            [(request.TargetDocumentType, request.TargetDocumentId, request.Amount)],
            cancellationToken);

        var forex = await PaymentForexCalculator.CalculateAsync(
            db, request.OrganizationId, direction, sourceCurrencyCode, sourceExchangeRate, targets, cancellationToken);

        if (forex is null)
        {
            return [];
        }

        var controlAccountId = await PaymentAccountResolver.ResolveControlAccountAsync(
            db, request.OrganizationId, direction, cancellationToken);

        return PaymentPostingRule.ForexLines(controlAccountId, forex);
    }
}
