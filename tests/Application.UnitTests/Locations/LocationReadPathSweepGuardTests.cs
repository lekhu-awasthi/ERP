using System.Reflection;
using ErpApp.Application.Common.Locations;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.Locations;

/// <summary>
/// Phase 35a's <b>read-side</b> sweep guard, and the half phase 32 did not have.
///
/// <para><c>LocationBearingCommandSweepGuardTests</c> proves every location-bearing type has a
/// command that can <i>write</i> a location. That guard was green throughout phase 32, 32b, 33 and
/// 34 while <b>fourteen of the fifteen document detail queries silently dropped the field on the
/// way back out</b>: each projects an explicit DTO, and only <c>InvoiceDetailDto</c> named
/// <c>LocationId</c>. The write path was perfect and the form could never show what it had stored —
/// worse, an edit re-posted the picker's default over the stored branch. Phase 32's own carried
/// note calls this out for one type ("a detail query projecting a DTO drops it silently"); this
/// asserts it for all of them, in three places at once:</para>
///
/// <list type="number">
/// <item><b>the detail query</b>, so a form can show the stored location;</item>
/// <item><b>the conversion template</b>, so converting a document keeps its branch instead of
/// falling back to the tenant default;</item>
/// <item><b>the list query</b>, so the LOCATION column every live grid carries has a filter behind
/// it (confirm-live 2026-09-10).</item>
/// </list>
///
/// <para>Each is derived from <see cref="DocumentMechanisms.LocationBearing"/> rather than listed,
/// so a document type added later fails the build until someone decides all three answers.</para>
/// </summary>
public class LocationReadPathSweepGuardTests
{
    private static readonly Assembly ApplicationAssembly = typeof(ILocationBearingCommand).Assembly;

    /// <summary>
    /// The query-name stem per type, matching <c>LocationBearingCommandSweepGuardTests</c>'s
    /// command stems. The two opening-balance kinds are absent on purpose: they are line rows with
    /// no detail page, no conversion and no grid of their own -- their whole surface is the
    /// <c>CreateOrUpdate</c> command that guard already covers, and their two list queries are
    /// exempted below with the reason.
    /// </summary>
    private static readonly IReadOnlyDictionary<DocumentType, string> QueryStems =
        new Dictionary<DocumentType, string>
        {
            [DocumentType.Quotation] = "Quotation",
            [DocumentType.SalesOrder] = "SalesOrder",
            [DocumentType.Invoice] = "Invoice",
            [DocumentType.CreditNote] = "CreditNote",
            [DocumentType.Payment] = "Payment",
            [DocumentType.PurchaseOrder] = "PurchaseOrder",
            [DocumentType.PurchaseBill] = "PurchaseBill",
            [DocumentType.Expense] = "Expense",
            [DocumentType.DebitNote] = "DebitNote",
            [DocumentType.JournalVoucher] = "JournalVoucher",
            [DocumentType.CashTransfer] = "CashTransfer",
            [DocumentType.WarehouseTransfer] = "WarehouseTransfer",
            [DocumentType.InventoryAdjustment] = "InventoryAdjustment",
            [DocumentType.ProductionOrder] = "ProductionOrder",
            [DocumentType.ProductionJournal] = "ProductionJournal",
        };

    /// <summary>
    /// Location-filtered queries that are deliberately <b>not</b> a document grid, each with the
    /// reason a user-facing location filter would be wrong there rather than merely missing.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ListFilterExempt =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ListAccountOpeningBalancesQuery"] =
                "Enumerates the whole chart of accounts with the opening line LEFT-joined on, so " +
                "location narrows the joined figures rather than the rows (phase-32b carried item " +
                "#2). A row filter here would hide accounts that merely have no opening balance at " +
                "the chosen branch.",
            ["ListProductOpeningBalancesQuery"] =
                "The product counterpart of ListAccountOpeningBalancesQuery, same shape and same " +
                "reasoning.",
            ["ListAllocatablePaymentsQuery"] =
                "A picker inside the Allocate dialog, not a list screen: it offers the credits that " +
                "can settle one document, and narrowing them by branch is the allocation policy " +
                "question phase 36 owns, not a grid filter.",
        };

    public static TheoryData<DocumentType> DocumentTypes => [.. QueryStems.Keys];

    private static Type Query(string name) =>
        ApplicationAssembly.GetTypes().SingleOrDefault(t => t.Name == name)
        ?? throw new InvalidOperationException($"No type named {name} in the Application assembly.");

    private static Type? ResponseOf(Type request) => request
        .GetInterfaces()
        .SingleOrDefault(i => i.IsGenericType && i.Name.StartsWith("IRequest`", StringComparison.Ordinal))
        ?.GetGenericArguments()[0];

    private static bool HasLocationId(Type type) =>
        type.GetProperty("LocationId", BindingFlags.Public | BindingFlags.Instance) is not null;

    [Fact]
    public void Every_location_bearing_document_type_has_a_query_stem()
    {
        var documents = DocumentMechanisms.LocationBearing
            .Except([DocumentType.OpeningBalance, DocumentType.OpeningStock])
            .OrderBy(x => x);

        Assert.Equal(documents, QueryStems.Keys.OrderBy(x => x));
    }

    [Theory]
    [MemberData(nameof(DocumentTypes))]
    public void The_detail_query_reads_the_stored_location_back(DocumentType documentType)
    {
        var response = ResponseOf(Query($"Get{QueryStems[documentType]}Query"));

        Assert.NotNull(response);
        Assert.True(
            HasLocationId(response),
            $"{response.Name} has no LocationId, so a {documentType} form can never show the branch " +
            "it was raised from -- and would post the picker's default over it on the next save.");
    }

    [Theory]
    [MemberData(nameof(DocumentTypes))]
    public void The_list_query_can_filter_by_location(DocumentType documentType)
    {
        var query = Query($"List{QueryStems[documentType]}sQuery");

        Assert.True(
            typeof(ILocationFilteredQuery).IsAssignableFrom(query),
            $"{query.Name} is a list over a location-bearing type and must declare " +
            "ILocationFilteredQuery -- see LocationScopeMarkers.");
        Assert.True(
            HasLocationId(query),
            $"{query.Name} has no LocationId, so the LOCATION column its grid shows has no filter " +
            "behind it.");
    }

    /// <summary>
    /// The other direction: nothing declares itself location-filtered and then offers no filter,
    /// unless it is named above with a reason. Phase-30's rule -- a guard asserted one way only
    /// proves half of what it claims.
    /// </summary>
    [Fact]
    public void No_location_filtered_query_lacks_a_filter_without_a_written_reason()
    {
        var missing = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(ILocationFilteredQuery).IsAssignableFrom(t)
                        && !HasLocationId(t)
                        && !ListFilterExempt.ContainsKey(t.Name))
            .Select(t => t.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_exemption_still_names_a_location_filtered_query()
    {
        var declared = ApplicationAssembly.GetTypes()
            .Where(t => typeof(ILocationFilteredQuery).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(ListFilterExempt.Keys.Where(name => !declared.Contains(name)));
    }

    /// <summary>
    /// Every conversion template carries the source document's location. There are five, found by
    /// name rather than listed, so a sixth added later is covered the day it appears.
    /// </summary>
    [Fact]
    public void Every_conversion_template_carries_the_source_documents_location()
    {
        var templates = ApplicationAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("ConversionTemplateQuery", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(5, templates.Count);

        foreach (var template in templates)
        {
            var response = ResponseOf(template);
            Assert.NotNull(response);
            Assert.True(
                HasLocationId(response),
                $"{response.Name} drops the source document's location, so converting moves the " +
                "document to the tenant's default branch without saying so.");
        }
    }
}
