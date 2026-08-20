using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.CreateOrUpdateOpeningBalanceLine;

/// <summary>
/// Editing an existing line reverses its own prior posting first (GlJournalEntry.PostReversalOf,
/// mirroring the posted lines exactly -- the same Phase 16a mechanism every Void already uses, not
/// a hand-derived reversal) before posting the corrected one, so GlLines/TrialBalance always
/// reflect only the latest value with no manual netting needed.
/// </summary>
public sealed class CreateOrUpdateOpeningBalanceLineCommandHandler(IAppDbContext db, IDocumentNumberGenerator numberGenerator)
    : IRequestHandler<CreateOrUpdateOpeningBalanceLineCommand, OpeningBalanceLineResult>
{
    private const string EquityAccountGroupName = "Opening Balance Equity";
    private const string EquityAccountName = "Opening Balance Equity";

    public async Task<OpeningBalanceLineResult> Handle(
        CreateOrUpdateOpeningBalanceLineCommand request, CancellationToken cancellationToken)
    {
        await AccountingValidation.EnsureAccountsExistAsync(db, request.OrganizationId, [request.AccountId], cancellationToken);

        var line = await db.OpeningBalanceLines.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId && x.AccountId == request.AccountId, cancellationToken);

        if (line is not null)
        {
            var priorEntry = await db.GlJournalEntries
                .Include(x => x.Lines)
                .SingleAsync(
                    x => x.SourceDocumentType == DocumentType.OpeningBalance && x.SourceDocumentId == line.Id, cancellationToken);
            db.GlJournalEntries.Add(GlJournalEntry.PostReversalOf(priorEntry));

            line.Update(request.Debit, request.Credit);
        }
        else
        {
            line = OpeningBalanceLine.Create(request.OrganizationId, request.AccountId, request.Debit, request.Credit);
            db.OpeningBalanceLines.Add(line);
        }

        var equityAccountId = await EnsureEquityAccountAsync(request.OrganizationId, cancellationToken);

        var lines = request.Debit > 0
            ? new List<GlLineInput> { new(request.AccountId, request.Debit, 0m), new(equityAccountId, 0m, request.Debit) }
            : new List<GlLineInput> { new(equityAccountId, request.Credit, 0m), new(request.AccountId, 0m, request.Credit) };

        db.GlJournalEntries.Add(GlJournalEntry.Post(request.OrganizationId, DocumentType.OpeningBalance, line.Id, lines));

        await db.SaveChangesAsync(cancellationToken);

        return new OpeningBalanceLineResult(line.Id, line.AccountId, line.Debit, line.Credit);
    }

    /// <summary>Finds or auto-provisions the tenant's single "Opening Balance Equity" contra
    /// account (an Equity-rooted group + account, created on first use) -- every opening-balance
    /// posting's balancing leg. Looked up by name rather than seeded at Organization creation,
    /// since Organizations start with zero AccountGroups/Accounts (fully user-managed Chart of
    /// Accounts, confirmed live -- no default seed exists anywhere in this codebase).</summary>
    private async Task<Guid> EnsureEquityAccountAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var existing = await db.Accounts.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.Name == EquityAccountName, cancellationToken);

        if (existing is not null)
        {
            return existing.Id;
        }

        var group = await db.AccountGroups.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.Name == EquityAccountGroupName, cancellationToken);

        if (group is null)
        {
            group = AccountGroup.Create(organizationId, EquityAccountGroupName, AccountRootType.Equity, null);
            db.AccountGroups.Add(group);
        }

        var code = await numberGenerator.GetNextNumberAsync(organizationId, DocumentType.Account, cancellationToken);
        var account = Account.Create(organizationId, code, EquityAccountName, AccountRootType.Equity, group.Id);
        db.Accounts.Add(account);

        return account.Id;
    }
}
