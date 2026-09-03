using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.Inventory.Reports;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.ExceptionalReport;

public sealed class ExceptionalReportQueryHandler(IAppDbContext db)
    : IRequestHandler<ExceptionalReportQuery, ExceptionalReportDto>
{
    /// <summary>
    /// The threshold below which a non-zero account balance is reported as a "Minor Account Balance
    /// Exception" -- a rounding residue nobody posted on purpose, which is what the row is scanning
    /// for. One rupee is this codebase's own smallest meaningful money unit, and stating the number
    /// here beats hiding it in a predicate: the live report gives no threshold, so this is a
    /// declared choice rather than a reproduction.
    /// </summary>
    public const decimal MinorBalanceThreshold = 1m;

    /// <summary>One account with the facts every predicate below needs.</summary>
    private sealed record AccountBalance(AccountRootType RootType, AccountKind Kind, bool IsActive, decimal Net);

    public async Task<ExceptionalReportDto> Handle(ExceptionalReportQuery request, CancellationToken cancellationToken)
    {
        var balances = await AccountBalancesAsync(request.OrganizationId, request.ToDate, cancellationToken);
        var customers = await ContactSidesAsync(request.OrganizationId, ContactType.Customer, request.ToDate, cancellationToken);
        var suppliers = await ContactSidesAsync(request.OrganizationId, ContactType.Supplier, request.ToDate, cancellationToken);
        var (inactiveStockValue, negativeStockQuantity) = await StockExceptionsAsync(
            request.OrganizationId, request.ToDate, cancellationToken);

        // Each account row is a predicate over the one pass above. Net is a signed net debit, so
        // "> 0" reads as a debit balance and "< 0" as a credit balance throughout.
        decimal SumWhere(Func<AccountBalance, bool> predicate) =>
            balances.Where(predicate).Sum(a => a.Net);

        List<ExceptionalReportRowDto> rows =
        [
            Ledger("Inactive Accounts with Outstanding Balances",
                SumWhere(a => !a.IsActive && a.Net != 0)),

            Ledger("Minor Account Balance Exception",
                SumWhere(a => a.Net != 0 && Math.Abs(a.Net) < MinorBalanceThreshold)),

            Ledger("Expense Accounts with Credit Balances",
                SumWhere(a => a.RootType == AccountRootType.Expense && a.Net < 0)),

            Ledger("Income Accounts with Debit Balances",
                SumWhere(a => a.RootType == AccountRootType.Income && a.Net > 0)),

            Ledger("Asset Accounts with Credit Balances",
                SumWhere(a => a.RootType == AccountRootType.Asset && a.Net < 0)),

            Ledger("Liability Accounts with Debit Balances",
                SumWhere(a => a.RootType == AccountRootType.Liability && a.Net > 0)),

            // A customer in credit is a negative signed balance -- ContactLedgerReader.BalanceType
            // is the statement of that convention -- so this is reported as a credit magnitude.
            Ledger("Customers with Credit Balances", -customers.Credit),

            Ledger("Bank and Cash Accounts with Negative Balances",
                SumWhere(a => a.Kind is AccountKind.Bank or AccountKind.Cash && a.Net < 0)),

            // A supplier we have overpaid holds a debit balance on their side of the convention.
            Ledger("Suppliers with Debit Balances", suppliers.Credit),

            // The two stock rows carry no DR/CR marker on the live report, and none is invented
            // here: a stock valuation and a quantity do not sit on a side of the ledger.
            new("Inactive Inventory Items with Balances", inactiveStockValue, BalanceType: null),
            new("Negative Inventory Balances", negativeStockQuantity, BalanceType: null),

            // The twelfth row. "Non-actionable" describes an account a user cannot post to or
            // correct, which is a concept this codebase's chart of accounts does not have: every
            // Account here is postable. Rather than invent a definition or drop a row from a
            // twelve-row fixed report, it ships as a real row flagged un-modelled, so the screen can
            // say why it is zero. Phase-26b's Service Charge precedent, one step further: there the
            // column was omitted because it had a sibling, here the row's absence would silently
            // change the report's identity.
            new("Non-actionable Account Balances", 0, GlBalanceMarker.For(0), IsModelled: false),
        ];

        return new ExceptionalReportDto(request.FromDate, request.ToDate, rows);
    }

    /// <summary>A ledger row: a non-negative magnitude plus the side it leans, GlBalanceMarker's split.</summary>
    private static ExceptionalReportRowDto Ledger(string particulars, decimal net) =>
        new(particulars, GlBalanceMarker.Magnitude(net), GlBalanceMarker.For(net));

    private async Task<List<AccountBalance>> AccountBalancesAsync(
        Guid organizationId, DateOnly asOf, CancellationToken cancellationToken)
    {
        // Inactive accounts are deliberately included -- the first row exists to find them.
        var accounts = await db.Accounts
            .Where(a => a.OrganizationId == organizationId)
            .Select(a => new { a.Id, a.RootType, a.Kind, a.IsActive })
            .ToListAsync(cancellationToken);

        var cutoff = GlDateBoundary.EndOfDayUtc(asOf);
        var glTotals = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == organizationId && entry.PostedAt <= cutoff
            group line by line.AccountId into g
            select new { AccountId = g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);
        var netByAccount = glTotals.ToDictionary(x => x.AccountId, x => x.Net);

        return [.. accounts.Select(a => new AccountBalance(
            a.RootType, a.Kind, a.IsActive, netByAccount.GetValueOrDefault(a.Id)))];
    }

    /// <summary>
    /// The two sides of one contact type's closing balances, read through the same
    /// <c>ContactLedgerReader</c> that Net Trading Assets and Customer Receivable Summary use -- so
    /// "Customers with Credit Balances" here is the same figure those reports show for the same
    /// customers.
    /// </summary>
    private async Task<(decimal Debit, decimal Credit)> ContactSidesAsync(
        Guid organizationId, ContactType contactType, DateOnly asOf, CancellationToken cancellationToken)
    {
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == organizationId && x.Type == contactType)
            .Select(x => new { x.Id, x.OpeningBalance })
            .ToListAsync(cancellationToken);

        var events = await ContactLedgerReader.LoadAllContactEventsAsync(
            db, organizationId, contactType, asOf, cancellationToken);
        var movementByContact = events
            .GroupBy(x => x.ContactId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.SignedAmount));

        decimal debit = 0, credit = 0;
        foreach (var contact in contacts)
        {
            var balance = contact.OpeningBalance + movementByContact.GetValueOrDefault(contact.Id);
            if (balance > 0)
            {
                debit += balance;
            }
            else
            {
                credit += balance;
            }
        }

        return (debit, credit);
    }

    /// <summary>
    /// Both stock rows from one <c>StockFactReader</c> pass: the value held by products that have
    /// been deactivated, and the total quantity by which products have gone negative (reported as a
    /// magnitude, since the row's own name already says which way it points).
    /// </summary>
    private async Task<(decimal InactiveValue, decimal NegativeQuantity)> StockExceptionsAsync(
        Guid organizationId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var products = await InventoryReportProducts.LoadAsync(
            db, organizationId, categoryId: null, productId: null, cancellationToken);

        var movements = await StockFactReader.LoadMovementsAsync(
            db, organizationId, productIds: null, warehouseId: null, asOf, cancellationToken);
        var facts = StockFactReader.Summarise(movements, asOf);

        decimal inactiveValue = 0, negativeQuantity = 0;
        foreach (var fact in facts)
        {
            if (fact.BalanceQuantity < 0)
            {
                negativeQuantity += -fact.BalanceQuantity;
            }

            if (products.For(fact.ProductId) is { IsActive: false } && fact.BalanceQuantity != 0)
            {
                inactiveValue += fact.BalanceValue;
            }
        }

        return (inactiveValue, negativeQuantity);
    }
}
