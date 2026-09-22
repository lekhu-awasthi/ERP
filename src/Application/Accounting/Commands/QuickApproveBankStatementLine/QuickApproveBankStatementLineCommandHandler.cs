using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.QuickApproveBankStatementLine;

/// <summary>
/// See <see cref="QuickApproveBankStatementLineCommand"/> for the shape of the feature and every
/// decision behind it. This file is the sequence: create, approve, find what the approval posted
/// against the bank account, reconcile the line against it.
/// </summary>
public sealed class QuickApproveBankStatementLineCommandHandler(
    IAppDbContext db,
    ISender sender,
    ICurrentUserService currentUser)
    : IRequestHandler<QuickApproveBankStatementLineCommand, QuickApproveBankStatementLineResult>
{
    public async Task<QuickApproveBankStatementLineResult> Handle(
        QuickApproveBankStatementLineCommand request, CancellationToken cancellationToken)
    {
        var line = await db.BankStatementLines.SingleOrDefaultAsync(
            x => x.Id == request.StatementLineId
                 && x.OrganizationId == request.OrganizationId
                 && x.BankAccountId == request.BankAccountId,
            cancellationToken)
            ?? throw new NotFoundException("Bank statement line not found on this account.");

        // Checked here as well as in the writer, because reaching the writer means a document has
        // already been created and approved: a 409 raised after that has posted to the ledger is a
        // document nobody asked for. The writer's copy stays as the backstop for the race.
        if (line.ReconciliationId is not null)
        {
            throw new ConflictException(
                "This bank statement line is already reconciled. Unreconcile it first.");
        }

        var accountExists = await db.Accounts.AnyAsync(
            x => x.Id == request.BankAccountId && x.OrganizationId == request.OrganizationId,
            cancellationToken);

        if (!accountExists)
        {
            throw new NotFoundException("Bank account not found.");
        }

        var amount = Math.Abs(line.Amount.Signed);

        var (documentType, documentId, documentCode) = request.Target switch
        {
            QuickApproveTarget.Contact =>
                await CreateAndApprovePaymentAsync(request, line, amount, cancellationToken),
            QuickApproveTarget.Account =>
                await CreateAndApproveJournalVoucherAsync(request, line, amount, cancellationToken),
            _ => throw new NotFoundException("Unknown quick-approve target."),
        };

        // What the approval actually posted against this bank account, read back rather than
        // assumed: phase 36's rule is that "one GL entry per Approved document" is a habit and not
        // an invariant, and a Payment's forex leg is the standing counterexample. Every line of
        // every entry the document posted, restricted to this account, is the book side -- which is
        // also why the sum can be trusted to equal the statement line's amount rather than the
        // document's.
        var entries = await db.GlJournalEntries
            .Where(x => x.SourceDocumentType == documentType && x.SourceDocumentId == documentId)
            .Include(x => x.Lines)
            .ToListAsync(cancellationToken);

        var bookLines = entries
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountId == request.BankAccountId)
            .ToList();

        if (bookLines.Count == 0)
        {
            // Unreachable through either route today -- both posting rules touch the bank account by
            // construction -- and stated rather than left as an empty-collection failure inside the
            // aggregate, because the sentence is what a future third route would need to read.
            throw new ConflictException(
                "The document was created but posted nothing against this bank account, so there is "
                + "nothing to reconcile it against.");
        }

        var write = BankReconciliationWriter.Reconcile(
            request.OrganizationId,
            request.BankAccountId,
            [line],
            bookLines,
            currentUser.UserId,
            DateTimeOffset.UtcNow,
            nameof(request.StatementLineId));

        db.BankReconciliations.Add(write.Reconciliation);
        await db.SaveChangesAsync(cancellationToken);

        return new QuickApproveBankStatementLineResult(
            line.Id, documentType, documentId, documentCode, write.Reconciliation.Id, amount);
    }

    /// <summary>
    /// A deposit is money received, a withdrawal is money paid. The contact's own type is not
    /// checked here: <c>CreatePaymentCommand</c> requires a Customer for Received and a Supplier for
    /// Paid, and a second copy of that rule could only drift from it.
    /// </summary>
    private async Task<(DocumentType Type, Guid Id, string Code)> CreateAndApprovePaymentAsync(
        QuickApproveBankStatementLineCommand request,
        BankStatementLine line,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var created = await sender.Send(
            new CreatePaymentCommand(
                request.OrganizationId,
                request.TargetId,
                line.Amount.IsDeposit ? PaymentDirection.Received : PaymentDirection.Paid,
                line.Date,
                PaymentModeId: null,
                AccountId: request.BankAccountId,
                amount,
                Reference: line.Description,
                Allocations: [])
            {
                LocationId = request.LocationId,
            },
            cancellationToken);

        var approved = await sender.Send(
            new ApprovePaymentCommand(request.OrganizationId, created.Id), cancellationToken);

        return (DocumentType.Payment, approved.Id, approved.Code);
    }

    /// <summary>
    /// A deposit debits the bank account and credits the chosen one; a withdrawal is the mirror.
    /// Two lines, which is the smallest legal Journal Voucher and exactly what the vendor's generic
    /// document carries (<c>items:[{account_id, amount}]</c> against the statement's own account).
    /// </summary>
    private async Task<(DocumentType Type, Guid Id, string Code)> CreateAndApproveJournalVoucherAsync(
        QuickApproveBankStatementLineCommand request,
        BankStatementLine line,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var lines = line.Amount.IsDeposit
            ?
            [
                new JournalVoucherLineInput(request.BankAccountId, amount, 0m),
                new JournalVoucherLineInput(request.TargetId, 0m, amount),
            ]
            : new List<JournalVoucherLineInput>
            {
                new(request.TargetId, amount, 0m),
                new(request.BankAccountId, 0m, amount),
            };

        var created = await sender.Send(
            new CreateJournalVoucherCommand(
                request.OrganizationId, line.Date, line.Description, lines)
            {
                LocationId = request.LocationId,
            },
            cancellationToken);

        var approved = await sender.Send(
            new ApproveJournalVoucherCommand(request.OrganizationId, created.Id), cancellationToken);

        return (DocumentType.JournalVoucher, approved.Id, approved.Code);
    }
}
