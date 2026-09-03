using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.Inventory.Reports;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.NetTradingAssets;

public sealed class NetTradingAssetsQueryHandler(IAppDbContext db)
    : IRequestHandler<NetTradingAssetsQuery, NetTradingAssetsDto>
{
    /// <summary>The four leaf figures at one date, before they are grouped into rows.</summary>
    private sealed record Position(
        decimal ReceivablesFromCustomers,
        decimal AdvanceToSuppliers,
        decimal PayableToSuppliers,
        decimal AdvanceFromCustomers,
        decimal Inventory);

    public async Task<NetTradingAssetsDto> Handle(NetTradingAssetsQuery request, CancellationToken cancellationToken)
    {
        var current = await PositionAsync(request.OrganizationId, request.ToDate, cancellationToken);

        // One request, not two: the comparison window is a second pass inside this handler and is
        // merged into the same response. Lining two responses up in the browser would mean
        // re-deriving the row set and the grouping client-side -- phase-26a's rule.
        var compareAsOf = request.Compare ? ComparePeriod.PriorYearAsOf(request.ToDate) : (DateOnly?)null;
        var comparison = compareAsOf is { } date
            ? await PositionAsync(request.OrganizationId, date, cancellationToken)
            : null;

        var rows = BuildRows(current, comparison, request.ExcludeAdvance);

        return new NetTradingAssetsDto(
            request.FromDate, request.ToDate, request.ExcludeAdvance, compareAsOf, rows);
    }

    private async Task<Position> PositionAsync(Guid organizationId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var (customerDebit, customerCredit) = await ContactSidesAsync(
            organizationId, ContactType.Customer, asOf, cancellationToken);
        var (supplierDebit, supplierCredit) = await ContactSidesAsync(
            organizationId, ContactType.Supplier, asOf, cancellationToken);

        var movements = await StockFactReader.LoadMovementsAsync(
            db, organizationId, productIds: null, warehouseId: null, asOf, cancellationToken);
        var inventory = StockFactReader.Summarise(movements, asOf).Sum(f => f.BalanceValue);

        // A positive signed balance means a debit for a customer and a credit for a supplier --
        // ContactLedgerReader.BalanceType is the statement of that convention. So a customer's debit
        // side is money owed to us, their credit side is money held on their behalf, and the
        // supplier reading is the mirror.
        return new Position(
            ReceivablesFromCustomers: customerDebit,
            AdvanceToSuppliers: supplierCredit,
            PayableToSuppliers: supplierDebit,
            AdvanceFromCustomers: customerCredit,
            Inventory: inventory);
    }

    /// <summary>
    /// Splits one contact type's closing balances into the two sides, using
    /// <c>ContactLedgerReader</c> exactly as <c>ContactBalanceSummaryQueryHandler</c> does -- opening
    /// balance plus every ledger event up to the date. Contacts are never netted against each other:
    /// a customer who owes and a customer in credit are two different facts, and adding them would
    /// hide both.
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
            // Only a strictly non-zero balance contributes. `else { credit += -balance; }` would
            // be equivalent arithmetically but not in decimal: `-0m` keeps its sign bit, so a
            // contact sitting at exactly zero produced a negative zero that surfaced as "-0" in the
            // .xlsx and "-0.00" on screen -- a figure a reader is right to distrust.
            if (balance > 0)
            {
                debit += balance;
            }
            else if (balance < 0)
            {
                credit -= balance;
            }
        }

        return (debit, credit);
    }

    private static IReadOnlyList<NetTradingAssetsRowDto> BuildRows(
        Position current, Position? comparison, bool excludeAdvance)
    {
        decimal? Compare(Func<Position, decimal> select) => comparison is null ? null : select(comparison);

        List<NetTradingAssetsRowDto> receivableChildren =
        [
            new("Receivables from Customers", current.ReceivablesFromCustomers,
                Compare(p => p.ReceivablesFromCustomers), []),
        ];
        List<NetTradingAssetsRowDto> payableChildren =
        [
            new("Payable to Suppliers", current.PayableToSuppliers, Compare(p => p.PayableToSuppliers), []),
        ];

        if (!excludeAdvance)
        {
            receivableChildren.Add(new(
                "Advance to Suppliers", current.AdvanceToSuppliers, Compare(p => p.AdvanceToSuppliers), []));
            payableChildren.Add(new(
                "Advance from Customers", current.AdvanceFromCustomers, Compare(p => p.AdvanceFromCustomers), []));
        }

        decimal Receivables(Position p) =>
            p.ReceivablesFromCustomers + (excludeAdvance ? 0 : p.AdvanceToSuppliers);
        decimal Payables(Position p) =>
            p.PayableToSuppliers + (excludeAdvance ? 0 : p.AdvanceFromCustomers);
        decimal Net(Position p) => Receivables(p) - Payables(p) + p.Inventory;

        return
        [
            new("Receivables", Receivables(current), Compare(Receivables), receivableChildren),
            new("Payables", Payables(current), Compare(Payables), payableChildren),
            new("Inventory Items", current.Inventory, Compare(p => p.Inventory), []),
            new("Net Trading Assets", Net(current), Compare(Net), []),
        ];
    }
}
