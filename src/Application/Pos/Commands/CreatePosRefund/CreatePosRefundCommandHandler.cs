using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos.Refunds;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Application.Sales.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.CreatePosRefund;

/// <summary>
/// Phase 63 -- plans the refund through <see cref="PosRefundPlanner"/> (the same plan the till's
/// preview showed), pays it out, then approves it through <see cref="CreditNoteApprovalPosting"/> (the
/// ERP's Approve path) and posts the payout as a second entry. One <c>SaveChangesAsync</c> commits all
/// of it; the only thing that can outlive a refusal is a document number, as with the sale.
///
/// <para>Cash paid out may not exceed what the drawer should hold: a refund that empties a drawer
/// below zero is a count that can never agree.</para>
/// </summary>
public sealed class CreatePosRefundCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<CreditNotePostingInput> postingRule,
    IGlPostingRule<CreditNotePayoutPostingInput> payoutPostingRule,
    IStockLedgerService stockLedgerService)
    : IRequestHandler<CreatePosRefundCommand, CreatePosRefundResult>
{
    public async Task<CreatePosRefundResult> Handle(CreatePosRefundCommand request, CancellationToken cancellationToken)
    {
        var session = await PosSessionAccess.LoadOwnOpenAsync(
            db, request.OrganizationId, request.SessionId, currentUser.UserId, cancellationToken);

        if (request.LocationId != session.BillingLocationId)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.LocationId), $"Session {session.Code} is at another location; a refund is paid out where its drawer is.")]);
        }

        var till = await PosTill.LoadAsync(db, request.OrganizationId, session.BillingLocationId, cancellationToken);

        // Decision E -- the note is created approved, so Approve is asked for at this branch every time.
        await GrantedPermissionReader.EnsureGrantedAtLocationAsync(
            db, request.OrganizationId, currentUser.UserId, PermissionKeys.CreditNoteApprove, till.Location.Id,
            cancellationToken);

        var plan = await PosRefundPlanner.PlanAsync(
            db, request.OrganizationId, till, session.Id, request.InvoiceId, request.Lines, request.Reason, request.Date,
            cancellationToken);
        var creditNote = plan.CreditNote;

        var payouts = await PosTenderModes.ResolveAsync(
            db, request.OrganizationId, till, session,
            [.. request.Payouts.Select(x => (x.PaymentModeId, x.Amount))],
            nameof(request.Payouts), cancellationToken);

        try
        {
            creditNote.PayOut(payouts, plan.RequiredPayout);
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Payouts), ex.Message)]);
        }

        await EnsureDrawerHoldsAsync(session, creditNote, cancellationToken);

        // Touches the session's rowversion: a refund and a concurrent close cannot both commit.
        session.RecordActivity();

        db.CreditNotes.Add(creditNote);

        await CreditNoteApprovalPosting.ApproveAndPostAsync(
            db, numberGenerator, postingRule, stockLedgerService, currentUser.UserId, creditNote, cancellationToken);

        await PostPayoutsAsync(creditNote, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new CreatePosRefundResult(
            creditNote.Id, creditNote.Code, creditNote.GrandTotal, creditNote.ServiceChargeTotal, creditNote.RoundOff,
            creditNote.PaidOutAmount, creditNote.ToAccountAmount);
    }

    private async Task EnsureDrawerHoldsAsync(PosSession session, CreditNote creditNote, CancellationToken cancellationToken)
    {
        var cashOut = creditNote.Payouts.Where(x => x.Kind == PaymentModeKind.Cash).Sum(x => x.Amount);
        if (cashOut == 0m)
        {
            return;
        }

        var sales = await PosSalesReader.ForSessionAsync(db, session.OrganizationId, session.Id, cancellationToken);
        var inDrawer = PosSalesReader.ExpectedCash(session, sales);

        if (cashOut > inDrawer)
        {
            throw new ConflictException(
                $"Session {session.Code}'s drawer should hold {inDrawer:0.00}, less than the {cashOut:0.00} in cash this "
                + "refund pays out. Pay part of it in another mode, or bring cash into the drawer with a Cash In first.");
        }
    }

    /// <summary>The second entry. Nothing is posted when nothing was handed back.</summary>
    private async Task PostPayoutsAsync(CreditNote creditNote, CancellationToken cancellationToken)
    {
        if (creditNote.Payouts.Count == 0)
        {
            return;
        }

        var receivableAccountId = await db.TenantSettings
            .Where(x => x.OrganizationId == creditNote.OrganizationId)
            .Select(x => x.DefaultAccountsReceivableId)
            .SingleAsync(cancellationToken)
            ?? throw new ConflictException("Default Accounts Receivable account is not configured.");

        var lines = payoutPostingRule.BuildLines(new CreditNotePayoutPostingInput(
            receivableAccountId,
            [.. creditNote.Payouts.Select(x => new InvoiceTenderPostingLine(x.AccountId, x.Amount))]));

        if (lines.Count == 0)
        {
            return;
        }

        db.GlJournalEntries.Add(GlJournalEntry.Post(
            creditNote.OrganizationId, DocumentType.CreditNote, creditNote.Id, lines, creditNote.LocationId));
    }
}
