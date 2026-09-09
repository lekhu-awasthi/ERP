using ErpApp.Application.Platform.Queries.GlobalSearch;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.Platform;

/// <summary>
/// Phase 33's sweep guard, in the family of phase-27a's <c>DocumentMechanismSweepGuardTests</c> and
/// phase-32b's <c>LocationScopeSweepGuardTests</c>.
///
/// <para><b>What it is for.</b> The search's document half iterates
/// <see cref="DocumentMechanisms.Transactional"/> and dispatches through a fifteen-arm switch. A
/// later phase that adds a transactional document type gets no compiler error from that switch --
/// it gets an <c>ArgumentOutOfRangeException</c> at runtime, on a keystroke, in production. And the
/// generic query reads four properties by <b>string</b>, so a rename of <c>Code</c> on one aggregate
/// would compile everywhere and fail only when someone searched for that type. Both are exactly the
/// silent-drift shape a sweep guard converts into a build failure.</para>
/// </summary>
public class GlobalSearchSweepGuardTests
{
    /// <summary>
    /// The fifteen aggregates the handler's switch dispatches to, restated independently. Kept as a
    /// separate list on purpose: a guard that read the handler's own switch would agree with it by
    /// construction and prove nothing.
    /// </summary>
    private static readonly IReadOnlyDictionary<DocumentType, Type> SearchableAggregates =
        new Dictionary<DocumentType, Type>
        {
            [DocumentType.Quotation] = typeof(Domain.Sales.Quotation),
            [DocumentType.SalesOrder] = typeof(Domain.Sales.SalesOrder),
            [DocumentType.Invoice] = typeof(Domain.Sales.Invoice),
            [DocumentType.CreditNote] = typeof(Domain.Sales.CreditNote),
            [DocumentType.Payment] = typeof(Domain.Payments.Payment),
            [DocumentType.PurchaseOrder] = typeof(Domain.Purchasing.PurchaseOrder),
            [DocumentType.PurchaseBill] = typeof(Domain.Purchasing.PurchaseBill),
            [DocumentType.Expense] = typeof(Domain.Purchasing.Expense),
            [DocumentType.DebitNote] = typeof(Domain.Purchasing.DebitNote),
            [DocumentType.JournalVoucher] = typeof(Domain.Accounting.JournalVoucher),
            [DocumentType.CashTransfer] = typeof(Domain.Accounting.CashTransfer),
            [DocumentType.WarehouseTransfer] = typeof(Domain.Inventory.WarehouseTransfer),
            [DocumentType.InventoryAdjustment] = typeof(Domain.Inventory.InventoryAdjustment),
            [DocumentType.ProductionOrder] = typeof(Domain.Manufacturing.ProductionOrder),
            [DocumentType.ProductionJournal] = typeof(Domain.Manufacturing.ProductionJournal),
        };

    [Fact]
    public void Every_transactional_document_type_is_searchable()
    {
        var missing = DocumentMechanisms.Transactional
            .Where(x => !SearchableAggregates.ContainsKey(x))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These transactional document types have no arm in GlobalSearchQueryHandler's switch, so "
            + "searching for one of their numbers would throw rather than return it: "
            + string.Join(", ", missing));

        var extra = SearchableAggregates.Keys
            .Where(x => !DocumentMechanisms.Transactional.Contains(x))
            .ToList();

        Assert.True(
            extra.Count == 0,
            "These types are searchable but are not transactional, so DocumentPermissions has no "
            + "View key for them and the handler would throw before it ever queried: "
            + string.Join(", ", extra));
    }

    /// <summary>
    /// The generic query reads Id / Code / OrganizationId / LocationId through
    /// <c>EF.Property</c>, which is the documented remedy (phase-2 bugs #1/#5) for a handler generic
    /// over types sharing no interface -- and the price of it is that the names are strings. This
    /// pins that all four exist, with the expected type, on all fifteen aggregates.
    /// </summary>
    [Fact]
    public void Every_searchable_aggregate_really_has_the_four_properties_read_by_name()
    {
        var problems = new List<string>();

        foreach (var (documentType, aggregate) in SearchableAggregates)
        {
            Check(aggregate, GlobalSearchQueryHandler.PropertyNames.Id, typeof(Guid));
            Check(aggregate, GlobalSearchQueryHandler.PropertyNames.Code, typeof(string));
            Check(aggregate, GlobalSearchQueryHandler.PropertyNames.OrganizationId, typeof(Guid));
            Check(aggregate, GlobalSearchQueryHandler.PropertyNames.LocationId, typeof(Guid?));

            void Check(Type type, string name, Type expected)
            {
                var property = type.GetProperty(name);

                if (property is null)
                {
                    problems.Add($"{documentType}: {type.Name} has no {name}.");
                }
                else if (property.PropertyType != expected)
                {
                    problems.Add(
                        $"{documentType}: {type.Name}.{name} is {property.PropertyType.Name}, expected {expected.Name}.");
                }
            }
        }

        Assert.True(
            problems.Count == 0,
            "GlobalSearchQueryHandler reads these by string through EF.Property, so a mismatch is a "
            + "runtime translation failure on a keystroke, not a compile error:"
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }
}
