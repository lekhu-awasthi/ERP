using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>Shared by ApproveExpenseCommandHandler and PreviewExpenseGlPosting -- resolves the
/// tenant's Accounts Payable/VAT Receivable/TDS Payable accounts (an Expense line's own Account is
/// already known to the caller, no per-line resolution needed) into the pure ExpensePostingInput
/// ExpensePostingRule consumes.</summary>
internal static class ExpenseAccountResolver
{
    public static async Task<ExpensePostingInput> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        IEnumerable<(Guid AccountId, decimal Amount, decimal VatAmount)> lines,
        decimal tdsAmount,
        CancellationToken cancellationToken)
    {
        var lineList = lines.ToList();

        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        if (settings.DefaultAccountsPayableId is not { } accountsPayableId)
        {
            throw new ConflictException(
                "Default Accounts Payable account is not configured. Set it under Accounting Defaults before approving expenses.");
        }

        var totalVat = lineList.Sum(x => x.VatAmount);
        if (totalVat > 0 && settings.DefaultVatReceivableAccountId is null)
        {
            throw new ConflictException(
                "Default VAT Receivable account is not configured. Set it under Accounting Defaults before approving expenses with VAT.");
        }

        if (tdsAmount > 0 && settings.DefaultTdsPayableAccountId is null)
        {
            throw new ConflictException(
                "Default TDS Payable account is not configured. Set it under Accounting Defaults before approving expenses with TDS.");
        }

        var postingLines = lineList.Select(x => new ExpensePostingLineInput(x.AccountId, x.Amount, x.VatAmount)).ToList();

        return new ExpensePostingInput(
            accountsPayableId,
            settings.DefaultVatReceivableAccountId ?? Guid.Empty,
            settings.DefaultTdsPayableAccountId ?? Guid.Empty,
            tdsAmount,
            postingLines);
    }
}
