using System.Reflection;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Infrastructure.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ErpApp.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Phase 50 — the sentence both sides have carried since phase 40 and neither has tested.
///
/// <para><c>ISortableQuery</c>'s doc comment says an ordering may be offered <b>exactly when an index
/// already leads on <c>(OrganizationId, &lt;that column&gt;)</c></b>, and calls the menu "a reading of
/// Infrastructure's <c>TenantIndexConvention</c>". <c>TenantIndexConvention</c>'s doc comment says
/// which indexes it creates and why. Between them the rule is complete — and until this class it was
/// enforced by two prose paragraphs facing each other across an assembly boundary that no test
/// project could cross, because <c>Application.UnitTests</c> references Application only. That is
/// carried item #6 of phase 47 and the open half of phase 40.</para>
///
/// <para><b>What failing looks like.</b> Add <c>ListSort.Amount = "amount"</c> and ship the menu
/// entry; every existing test still passes, every screen still works, and the invoice list quietly
/// becomes a sort of fifty thousand rows on every page load — which the pager hides, because page 1
/// still returns fifty rows. That is the failure phase 34c measured from the other direction
/// (469&#160;ms to 49&#160;ms by adding the index the ordering needed), and it is what this class
/// makes loud.</para>
/// </summary>
[Collection(nameof(ModelCollection))]
public class ListSortIndexCorrespondenceTests(ModelFixture fixture)
{
    private static readonly Assembly ApplicationAssembly = typeof(IRequirePermission).Assembly;

    /// <summary>
    /// Every ordering the wire accepts, mapped to the column a handler orders by when it is asked
    /// for. <c>Newest</c> is <c>CreatedAt</c> on all fifteen; <c>DocumentDate</c> is the aggregate's
    /// own business date, which is resolved <b>from the model</b> below rather than from
    /// <c>TenantIndexConvention</c>'s name list — a test that asked the convention which column it
    /// chose and then checked it had indexed that column would pass by construction.
    /// </summary>
    /// <summary>
    /// What each wire value orders by. <c>null</c> means "this aggregate's own business date",
    /// resolved per entity by <see cref="BusinessDateOf"/>.
    ///
    /// <para>The test is driven from <see cref="ListSort.DocumentOrderings"/> through this map rather
    /// than from two hard-coded checks, and that is the whole difference between a guard and a
    /// decoration. Written the other way it asserted that <c>CreatedAt</c> and the business date are
    /// indexed — both of which are true and neither of which is the rule — so adding
    /// <c>ListSort.Amount</c> and shipping the menu entry passed it unchanged. Injecting exactly that
    /// regression is how this was found; see phase-50-status.md Decision C.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> OrderingColumns =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ListSort.Newest] = "CreatedAt",
            [ListSort.DocumentDate] = null,
        };

    [Fact]
    public void Every_ordering_a_document_list_offers_is_backed_by_an_index_that_leads_on_it()
    {
        var unmapped = ListSort.DocumentOrderings.Where(o => !OrderingColumns.ContainsKey(o)).ToList();

        Assert.True(
            unmapped.Count == 0,
            "ListSort has grown an ordering this test does not know how to check: "
            + string.Join(", ", unmapped)
            + ". Say which column a handler orders by when it is asked for that value (add it to "
            + "OrderingColumns), so the index behind it can be checked. An ordering nobody can name a "
            + "column for is an ordering nobody can index.");

        var unbacked = new List<string>();

        foreach (var queryType in SortableDocumentListQueries())
        {
            var entity = fixture.Entity(EntityFor(queryType).Name);

            foreach (var ordering in ListSort.DocumentOrderings)
            {
                var column = OrderingColumns[ordering] ?? BusinessDateOf(entity);

                if (column is null)
                {
                    unbacked.Add($"{queryType.Name} offers '{ordering}' but {entity.ClrType.Name} has "
                        + "no single required DateOnly property to order by.");
                }
                else if (!ModelFixture.HasIndexLeadingOn(entity, column))
                {
                    unbacked.Add($"{queryType.Name} offers '{ordering}' but no index on "
                        + $"{entity.ClrType.Name} leads on (OrganizationId, {column}).");
                }
            }
        }

        Assert.True(
            unbacked.Count == 0,
            "ISortableQuery's rule is that an ordering may be offered exactly when an index leads on "
            + "(OrganizationId, <that column>). These offer one that does not, so the screen sorts "
            + "the whole filtered set and the pager hides it:\n  "
            + string.Join("\n  ", unbacked));
    }

    /// <summary>
    /// The derivation has to resolve, or the test above is quietly checking nothing. Phase 39's
    /// lesson: a guard whose predicate stops matching stops covering, and reports success either way.
    /// </summary>
    [Fact]
    public void The_sweep_finds_the_document_lists_it_is_supposed_to_find()
    {
        var queries = SortableDocumentListQueries().ToList();

        // Sixteen, as of phase 55. Fifteen came from phase 47's sweep, which describes itself as
        // "fifteen queries over sixteen screens" because ListPaymentsQuery backs both the customer
        // and the supplier payment list; the one query it excludes is ListChequesQuery, whose
        // absence here is SortSweepGuardTests.Exempt -- see
        // The_cheque_exemptions_index_half_has_been_measured_away for what phase 50 changed about it.
        //
        // The sixteenth is phase 55's ListBankStatementLinesQuery. It is the first *parent-scoped*
        // list to reach this sweep -- a statement belongs to one bank account -- and it is in
        // rather than exempt because the test above proves its two orderings are index-backed:
        // BankStatementLine carries Date, so TenantIndexConvention derives both
        // (OrganizationId, Date) and (OrganizationId, CreatedAt DESC) for it with no hand-written
        // index at all.
        Assert.Equal(16, queries.Count);
        Assert.All(queries, q => Assert.NotNull(EntityFor(q)));
    }

    /// <summary>
    /// The reverse direction, and the one that would otherwise rot. <c>SortSweepGuardTests.Exempt</c>
    /// excuses <c>ListChequesQuery</c>, and until this phase it gave <b>two</b> reasons: the screen is
    /// a register rather than a document list, and the aggregate carried neither index the rule
    /// requires. Phase 50 measured the second one away — <c>Cheque</c> now leads on
    /// <c>(OrganizationId, ChequeDate)</c>. Only the screen-shape reason survives, and that one is
    /// not visible from the model, so what this asserts is the narrower thing that is: <b>the index
    /// half of that exemption is no longer a fact about the schema.</b> A reader who takes the old
    /// sentence at face value is reading something that stopped being true here.
    /// </summary>
    [Fact]
    public void The_cheque_exemptions_index_half_has_been_measured_away()
    {
        var cheque = fixture.Entity("Cheque");

        Assert.True(
            ModelFixture.HasIndexLeadingOn(cheque, "ChequeDate"),
            "Phase 50 added (OrganizationId, ChequeDate) on the strength of a measurement -- 2,001 "
            + "logical reads to 877 on the register's first page, 2,606 to 643 on a date range. If "
            + "it has gone, the numbers in docs/phase-50-status.md describe a schema that no longer "
            + "exists.");

        Assert.False(
            ModelFixture.HasIndexLeadingOn(cheque, "CreatedAt"),
            "The Cheque Register orders by ChequeDate and offers no Sort by menu, so (OrganizationId, "
            + "CreatedAt) would be an index nothing can ask for. Phase 50 declined to add it and "
            + "TenantIndexConvention.DeclaredBusinessDates carries the flag that says so; if this "
            + "fails, that declaration has been lost and the table is paying for a fourth index.");
    }

    /// <summary>
    /// A document list that offers an ordering, derived exactly as
    /// <c>SortSweepGuardTests.DocumentListQueries</c> derives its own set — phase 34b's rule that an
    /// aggregate with a business date is a document — and then narrowed to the ones that implement
    /// <see cref="ISortableQuery"/>. The two sweeps must see the same world or one of them is wrong,
    /// which is what <see cref="The_sweep_finds_the_document_lists_it_is_supposed_to_find"/> pins.
    /// </summary>
    private static IEnumerable<Type> SortableDocumentListQueries() =>
        ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => typeof(IDateRangeFilteredQuery).IsAssignableFrom(t))
            .Where(typeof(ISortableQuery).IsAssignableFrom)
            .Where(t => t.Name.StartsWith("List", StringComparison.Ordinal))
            .Where(t => t.Name.EndsWith("Query", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    /// <summary><c>ListPurchaseBillsQuery</c> → the <c>PurchaseBills</c> set → <c>PurchaseBill</c>.</summary>
    private static Type EntityFor(Type queryType)
    {
        var setName = queryType.Name["List".Length..^"Query".Length];

        var property = typeof(IAppDbContext).GetProperties()
            .FirstOrDefault(p => p.Name == setName)
            ?? throw new InvalidOperationException($"IAppDbContext has no '{setName}' set for {queryType.Name}.");

        return property.PropertyType.GetGenericArguments()[0];
    }

    /// <summary>
    /// The aggregate's business date, read off the model rather than asked of the convention.
    ///
    /// <para>The type and the nullability do most of the work: every other date a document carries is
    /// either an instant (<c>CreatedAt</c>, <c>ApprovedAt</c>, <c>VoidedAt</c>) or nullable
    /// (<c>ExpiryDate</c>, <c>DeliveryDate</c>). What they do not settle is <c>Invoice</c> and
    /// <c>PurchaseBill</c>, which carry a <b>required</b> <c>DueDate</c> beside their <c>Date</c> —
    /// phase 31 made it non-nullable and stored. So where there are two, the tie-break is the name
    /// <c>Date</c>, and where there is one it is taken whatever it is called. That second half is the
    /// half that matters: it is what lets this resolve <c>Cheque.ChequeDate</c> without reading
    /// <c>TenantIndexConvention.DeclaredBusinessDates</c>, which would make the assertion circular.
    /// An entity with two required dates and neither called <c>Date</c> returns null and is reported
    /// by the caller, never guessed at.</para>
    /// </summary>
    private static string? BusinessDateOf(IEntityType entity)
    {
        var required = entity.GetProperties()
            .Where(p => p.ClrType == typeof(DateOnly) && !p.IsNullable)
            .Select(p => p.Name)
            .ToList();

        return required.Count == 1
            ? required[0]
            : required.FirstOrDefault(n => n == "Date");
    }
}
