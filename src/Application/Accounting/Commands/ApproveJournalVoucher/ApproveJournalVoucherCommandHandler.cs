using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;

public sealed class ApproveJournalVoucherCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<JournalVoucher> postingRule,
    ICashBalancePolicy cashBalancePolicy)
    : IRequestHandler<ApproveJournalVoucherCommand, ApproveJournalVoucherResult>
{
    public async Task<ApproveJournalVoucherResult> Handle(ApproveJournalVoucherCommand request, CancellationToken cancellationToken)
    {
        var journalVoucher = await db.JournalVouchers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Journal voucher not found.");

        if (journalVoucher.Status != JournalVoucherStatus.Draft)
        {
            throw new ConflictException("Only a Draft journal voucher can be approved.");
        }

        // Fail fast with a friendly 409 before Approve()'s own (stricter, InvalidOperationException
        // -> 500) invariant check ever runs -- same two conditions, checked here first purely for
        // a clean HTTP status.
        if (journalVoucher.Lines.Count < 2
            || journalVoucher.Lines.Sum(x => x.Debit) != journalVoucher.Lines.Sum(x => x.Credit))
        {
            throw new ConflictException(
                "A journal voucher needs at least two lines with total Debit equal to total Credit to be approved.");
        }

        // Phase 31 -- NegativeCashBalanceAction. A voucher can credit anything, so the outflows are
        // its net credits per account; the policy itself drops every account that is not Bank or
        // Cash kind. Net, not gross, so a line pair that debits and credits the same account nets to
        // nothing rather than reading as a withdrawal.
        var voucherOutflows = journalVoucher.Lines
            .GroupBy(x => x.AccountId)
            .Select(g => new CashOutflow(
                g.Key,
                ExchangeRates.ToBase(g.Sum(x => x.Credit) - g.Sum(x => x.Debit), journalVoucher.ExchangeRate)))
            .ToList();

        var voucherStatus = await cashBalancePolicy.CheckAsync(
            request.OrganizationId, voucherOutflows, cancellationToken);

        CashBalanceGuard.Enforce(voucherStatus, request.OverrideNegativeCashBalanceWarning, "journal voucher");

        var code = await numberGenerator.GetNextNumberAsync(
            request.OrganizationId, DocumentType.JournalVoucher, cancellationToken, journalVoucher.LocationId);

        journalVoucher.Approve(currentUser.UserId, code);

        // Phase 28: JournalVoucher and CashTransfer are the two posting rules whose input is the
        // domain aggregate itself, so unlike Invoice/PurchaseBill/etc. there is no line-amount
        // argument to convert before the rule runs -- the finished line list is converted instead,
        // and any rounding residue is booked to the tenant's forex account rather than absorbed.
        // See GlCurrencyConversion for why the two paths differ and why only these two can produce
        // a residue at all.
        var glLines = await GlCurrencyConversion.ToBaseAsync(
            db, request.OrganizationId, postingRule.BuildLines(journalVoucher), journalVoucher.ExchangeRate,
            cancellationToken);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.JournalVoucher, journalVoucher.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveJournalVoucherResult(journalVoucher.Id, journalVoucher.Code, journalVoucher.Status, journalVoucher.ApprovedAt);
    }
}
