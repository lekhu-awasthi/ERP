using System.Reflection;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.Tenancy;

/// <summary>
/// Phase 41's sweep guard. A transaction quota that silently stops covering a document type is worse
/// than no quota: the tenant is billed for a ceiling and the twelfth document type walks through it,
/// and nothing anywhere fails.
///
/// <para>So this asserts, by reflection over the Application assembly, that
/// <see cref="DocumentMechanisms.MeteredTransactions"/> and the commands actually implementing
/// <see cref="IMeteredTransaction"/> are the same set -- in <b>both</b> directions, per phase 30's
/// rule that a guard asserted one way proves half of what it claims. Phase 39's lesson is why the
/// predicate names an interface rather than a type shape: a guard whose predicate names a
/// <i>type</i> stops covering anything solved before that type existed.</para>
///
/// <para>The third assertion is the one that catches the real future mistake. A document type that
/// starts posting to the GL without joining this list would be metered nowhere, so the list is also
/// checked against <c>GlSourceDocumentResolver</c>'s own thirteen -- named here as the two
/// deliberate exclusions, so adding a fourteenth GL-posting type fails until someone decides which
/// side it falls on.</para>
/// </summary>
public class MeteredTransactionSweepGuardTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IMeteredTransaction).Assembly;

    private static IReadOnlyList<Type> MeteredCommands => [.. ApplicationAssembly
        .GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IMeteredTransaction).IsAssignableFrom(t))
        .OrderBy(t => t.Name, StringComparer.Ordinal)];

    /// <summary>
    /// The two GL-posting source types that are deliberately <b>not</b> metered, each with its
    /// reason. Opening balances are setup, not trade: they have no Draft/Approve lifecycle to meter,
    /// and charging a tenant's annual allowance for entering its own opening position would bill it
    /// for migrating in.
    /// </summary>
    private static readonly IReadOnlyDictionary<DocumentType, string> UnmeteredPostingSources =
        new Dictionary<DocumentType, string>
        {
            [DocumentType.OpeningBalance] =
                "Opening setup, keyed by (OrganizationId, AccountId) and edited in place -- no Approve to meter.",
            [DocumentType.OpeningStock] =
                "Opening setup, as OpeningBalance; phase 37 posts a cost catch-up against it, not a trade.",
        };

    /// <summary>
    /// The thirteen types that reach <c>GlJournalEntry.Post</c>, per
    /// <c>GlSourceDocumentResolver</c>'s own doc comment (phase 26a, extended by phase 37).
    /// Restated here rather than derived, so this test and that resolver are two independently
    /// written things that have to agree -- the same reason phase 39 pinned two implementations of
    /// one rule to a shared table rather than to each other.
    /// </summary>
    private static readonly IReadOnlyList<DocumentType> GlPostingTypes =
    [
        DocumentType.Invoice,
        DocumentType.CreditNote,
        DocumentType.PurchaseBill,
        DocumentType.Expense,
        DocumentType.DebitNote,
        DocumentType.JournalVoucher,
        DocumentType.CashTransfer,
        DocumentType.InventoryAdjustment,
        DocumentType.Payment,
        DocumentType.ProductionJournal,
        DocumentType.WarehouseTransfer,
        DocumentType.OpeningBalance,
        DocumentType.OpeningStock,
    ];

    [Fact]
    public void Every_metered_document_type_has_exactly_one_command_implementing_the_marker()
    {
        var declared = MeteredCommands
            .Select(t => ((IMeteredTransaction)FormatterServicesStub.Uninitialized(t)).MeteredDocumentType)
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(DocumentMechanisms.MeteredTransactions.OrderBy(x => x), declared);
    }

    /// <summary>The other direction: nothing implements the marker without belonging to the list.
    /// Asserted as a set comparison above, and as a count here so a duplicate -- two commands both
    /// claiming Invoice -- cannot hide behind a matching set.</summary>
    [Fact]
    public void No_command_implements_the_marker_without_being_on_the_list()
    {
        Assert.Equal(DocumentMechanisms.MeteredTransactions.Count, MeteredCommands.Count);
    }

    /// <summary>
    /// Every type that posts to the GL is either metered or has a written reason not to be. This is
    /// the assertion that fails when a fourteenth posting type is added, which is the mistake a
    /// quota feature is actually exposed to.
    /// </summary>
    [Fact]
    public void Every_gl_posting_type_is_either_metered_or_excused()
    {
        var accountedFor = DocumentMechanisms.MeteredTransactions
            .Concat(UnmeteredPostingSources.Keys)
            .OrderBy(x => x);

        Assert.Equal(GlPostingTypes.OrderBy(x => x), accountedFor);
    }

    /// <summary>A metered type that is not transactional would have no Approve command to carry the
    /// marker, so the two lists must nest.</summary>
    [Fact]
    public void Every_metered_type_is_transactional()
    {
        Assert.All(
            DocumentMechanisms.MeteredTransactions,
            x => Assert.Contains(x, DocumentMechanisms.Transactional));
    }

    /// <summary>
    /// The four transactional types that approve without posting. Stated positively so the exclusion
    /// is a decision on the record rather than an absence -- and so that a later phase giving one of
    /// them a posting rule fails here.
    /// </summary>
    [Theory]
    [InlineData(DocumentType.Quotation)]
    [InlineData(DocumentType.SalesOrder)]
    [InlineData(DocumentType.PurchaseOrder)]
    [InlineData(DocumentType.ProductionOrder)]
    public void A_type_that_posts_nothing_is_not_metered(DocumentType documentType)
    {
        Assert.DoesNotContain(documentType, DocumentMechanisms.MeteredTransactions);
        Assert.DoesNotContain(documentType, GlPostingTypes);
    }

    /// <summary>
    /// Phase 46 -- the same question asked of the scan axis, which is why the ceiling is a marker
    /// interface rather than a type check in the behavior for its single implementer.
    ///
    /// <para>Extraction is the only action in this product that spends money outward per call, and
    /// the reference product's own AI page advertises scanning documents "individually or in bulk".
    /// So a second entry point is a foreseeable change, and the failure mode if one arrives unmarked
    /// is silent: the bulk path would be free, uncapped, and billed to whoever runs the deployment.
    /// This asserts the set is exactly what it is today, so adding one fails here until somebody
    /// decides whether it spends the allowance.</para>
    /// </summary>
    [Fact]
    public void The_scan_ceiling_covers_exactly_the_commands_that_call_the_extractor()
    {
        var metered = ApplicationAssembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IMeteredAiScan).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["ExtractInboxDocumentCommand"], metered);
    }

    /// <summary>
    /// Handlers that take <c>IDocumentExtractor</c> without ever spending a scan, each with its
    /// reason -- the same shape as <see cref="UnmeteredPostingSources"/> above, and written out for
    /// the same reason: an exclusion stated positively is a decision, an exclusion left out of a
    /// list is an oversight nobody can tell from a decision.
    ///
    /// <para><b>Taking the dependency is not the same as spending the allowance</b>, which is what
    /// the first draft of the guard below got wrong. Both of these read
    /// <c>IsConfigured</c> and <c>ModelId</c> -- local properties describing whether a credential
    /// exists and which model would be used -- to render the AI settings panel. Neither sends
    /// anything anywhere.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> UnmeteredExtractorConsumers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GetAiDocumentExtractionSettingQueryHandler"] =
                "Reads IsConfigured/ModelId to render the settings panel; makes no vendor call.",
            ["UpdateAiDocumentExtractionSettingCommandHandler"] =
                "Returns the same two metadata properties beside the saved flag; makes no vendor call.",
        };

    /// <summary>
    /// The other direction, and the one that would actually bite. A request that reaches
    /// <c>IDocumentExtractor</c> without implementing the marker is an uncapped paid call; this finds
    /// it by looking for the dependency rather than for the marker, so the two cannot agree merely
    /// because somebody updated both halves of one list.
    /// </summary>
    [Fact]
    public void Nothing_reaches_the_extractor_without_being_metered()
    {
        var extractorConsumers = ApplicationAssembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(
                param => param.ParameterType == typeof(ErpApp.Application.Common.DocumentExtraction.IDocumentExtractor))))
            .Select(t => t.Name)
            .Where(name => !UnmeteredExtractorConsumers.ContainsKey(name))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        // One handler, and it is the handler of the one marked command. If a second appears, its
        // command must implement IMeteredAiScan -- or this phase's ceiling has a hole in it.
        Assert.Equal(["ExtractInboxDocumentCommandHandler"], extractorConsumers);
    }

    /// <summary>Every named exclusion still exists, so a handler that is deleted or renamed cannot
    /// leave a stale excuse behind that silently exempts some future handler of the same name.</summary>
    [Fact]
    public void Every_unmetered_extractor_consumer_still_exists()
    {
        var present = ApplicationAssembly.GetTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(UnmeteredExtractorConsumers.Keys, name => Assert.Contains(name, present));
    }

    /// <summary>
    /// Phase 46 -- the three axes are deliberately not interchangeable, and this pins the one that
    /// is missing. Locations are sold per unit like products and transactions, but the reference
    /// product does not cap them (Cadehi runs three on an Enabled flag with an unbounded list), so
    /// <c>LocationQuota</c> is a record and there is no quota kind that could refuse one.
    /// </summary>
    [Fact]
    public void There_is_no_refusal_kind_for_locations_or_sms()
    {
        var kinds = Enum.GetNames<ErpApp.Application.Common.Exceptions.SubscriptionQuotaKind>();

        Assert.Equal(["AiScans", "Products", "Transactions"], kinds.OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>
    /// Creating a command record without running a constructor: every metered command is a positional
    /// record whose parameters differ, and this guard only ever reads an expression-bodied property
    /// that closes over a constant.
    /// </summary>
    private static class FormatterServicesStub
    {
        public static object Uninitialized(Type type) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
    }
}
