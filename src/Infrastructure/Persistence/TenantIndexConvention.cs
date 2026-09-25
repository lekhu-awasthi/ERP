using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ErpApp.Infrastructure.Persistence;

/// <summary>
/// Phase 34c (NFR-5.1). The tenant index sweep, applied as a rule over the model rather than as a
/// list of <c>HasIndex</c> calls spread across sixteen configuration folders.
///
/// <para><b>What the measurement found.</b> On a 50,000-invoice tenant, the invoice list's first
/// page cost 662&#160;ms p95 and its SQL read 2,644 pages — a full clustered scan, twice (once for
/// <c>CountAsync</c>, once for <c>Skip/Take</c>), plus a sort of every row in the tenant to satisfy
/// <c>ORDER BY CreatedAt DESC</c>. Eighteen tenant-scoped tables had <b>no index whose leading key
/// is OrganizationId at all</b>, and the eighteen were not a random eighteen: they are exactly the
/// transactional documents plus <see cref="Domain.Accounting.GlJournalEntry"/>. Every other
/// tenant-scoped table already had one <i>for free</i>, because master data carries a per-tenant
/// uniqueness rule — <c>(OrganizationId, Code)</c> or <c>(OrganizationId, Name)</c> — and a document
/// number is not unique-indexed. The gap tracked the absence of a uniqueness constraint, not
/// anyone's judgment about which tables are hot.</para>
///
/// <para><b>The rule.</b> An entity that carries <c>OrganizationId</c> <i>and</i> a business date is
/// a document: it gets <c>(OrganizationId, BusinessDate)</c> for the registers and statements that
/// range over that date, and <c>(OrganizationId, CreatedAt DESC)</c> for the list screen, whose
/// ordering is <c>CreatedAt DESC</c> on every one of them. Anything else is master data or a child
/// row and must already carry a leading-OrganizationId index — which this convention asserts rather
/// than assumes, failing model build if it does not.</para>
///
/// <para>"An aggregate with a business <c>Date</c> is a document, master data is not" is phase 34b's
/// own rule for which lists get a date filter (CLAUDE.md carries it). The same sentence decides
/// which tables get the date index, which is the reason to state it once here instead of deciding
/// it per table: the next document type is indexed the moment it is mapped.</para>
///
/// <para>Both indexes are two narrow columns over a clustered <c>Id</c>, and neither key column is
/// ever updated — <c>OrganizationId</c>, <c>CreatedAt</c> and the business date are all write-once —
/// so the write cost is two index inserts per document row and no update cost at all. See
/// phase-34c-status.md Decision B for the measured figure.</para>
/// </summary>
internal static class TenantIndexConvention
{
    public const string OrganizationId = "OrganizationId";
    public const string CreatedAt = "CreatedAt";

    /// <summary>
    /// The business-date column, in the order it is looked for. <c>Date</c> is what fourteen
    /// document aggregates call it; <c>PostedAt</c> is <see cref="Domain.Accounting.GlJournalEntry"/>'s
    /// name for the same thing, and is what all three financial statements filter on.
    ///
    /// <para><b>Phase 50.</b> This list was wrong, and it was wrong in three places rather than one.
    /// Phase 47 predicted the failure mode exactly — <i>a convention matching by name is a list, and
    /// a list becomes a wrong list</i> — having found that <see cref="Domain.Payments.Cheque"/>
    /// spells its date <c>ChequeDate</c> and so fell through to the master-data branch in silence.
    /// Asking the mirror question (which tenant-scoped entities carry a business date this list never
    /// looked at?) found two more: <c>StockLedgerEntry</c> and <c>StockMovement</c> both spell theirs
    /// <c>TransactionDate</c>. Those two were fine, but <b>by luck and not by rule</b> — each carries
    /// a hand-written <c>(OrganizationId, ProductId, WarehouseId, TransactionDate)</c> composite, so
    /// <see cref="HasLeadingTenantIndex"/> waved them through without anyone knowing a classification
    /// had been missed.</para>
    ///
    /// <para>So the name list stays — it is right for fourteen of seventeen and reading it is how you
    /// learn what a document is here — but it is no longer allowed to be <i>silently</i> wrong. An
    /// entity it fails to classify is declared in <see cref="DeclaredBusinessDates"/> or excused in
    /// <see cref="DatesThatAreNotBusinessDates"/>, and anything in neither fails the model build with
    /// the property named. The list can still be incomplete; it can no longer be incomplete without
    /// somebody being told.</para>
    /// </summary>
    private static readonly string[] BusinessDateNames = [nameof(Domain.Sales.Invoice.Date), "PostedAt"];

    /// <summary>
    /// A document whose business date <see cref="BusinessDateNames"/> cannot find, and what its list
    /// orders by — because the second half is not free either.
    ///
    /// <para><c>ListOrdersByCreatedAt</c> is the assumption the fourteen name-matched documents all
    /// satisfy and that the convention had baked in: every document list orders <c>CreatedAt DESC</c>,
    /// so every document table gets that index beside its date one. The Cheque Register does not —
    /// it orders by <c>ChequeDate DESC</c>, has no <c>Sort by</c> menu at all
    /// (<c>SortSweepGuardTests.Exempt</c> says why), and so would carry a second index that nothing
    /// on the screen could ever ask for. Phase 50 measured that it is not needed and declined to add
    /// it; an index nobody orders by is write cost plus one more way for the optimizer to change its
    /// mind about a plan (phase 34c's first finding).</para>
    /// </summary>
    private sealed record DeclaredDate(string Column, bool ListOrdersByCreatedAt);

    private static readonly IReadOnlyDictionary<string, DeclaredDate> DeclaredBusinessDates =
        new Dictionary<string, DeclaredDate>(StringComparer.Ordinal)
        {
            ["Cheque"] = new(nameof(Domain.Payments.Cheque.ChequeDate), ListOrdersByCreatedAt: false),
        };

    /// <summary>
    /// Tenant-scoped entities that carry a required date which is <b>not</b> a document's business
    /// date, each with the reason. This is the half that makes the name list safe: without it, "no
    /// business date found" and "master data" are the same answer, which is the state
    /// <see cref="Domain.Payments.Cheque"/> sat in from phase 17 to phase 50.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DatesThatAreNotBusinessDates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["StockLedgerEntry"] =
                "TransactionDate is a FIFO layer's date, and nothing reads a tenant's layers by date "
                + "alone -- every reader is keyed by product and warehouse first, which is what the "
                + "hand-written (OrganizationId, ProductId, WarehouseId, TransactionDate) composite "
                + "serves. Phase 26c: a dated stock report derives from StockMovement, not from here.",
            ["StockMovement"] =
                "The same shape and the same composite. This IS what a dated stock report reads, and "
                + "it reads it per product and warehouse -- a tenant-wide movement list by date is "
                + "not a screen.",
            ["PhysicalStockMovement"] =
                "Phase 58 -- StockMovement's shape for the physical ledger, with the same composite and "
                + "the same readers: per product and warehouse, never a tenant-wide list by date.",
            ["AlertSendLog"] =
                "OccurrenceDate is a job claim's key, not a business date: it exists to make "
                + "(AlertDefinitionId, OccurrenceDate, Recipient) unique so a send happens exactly "
                + "once (phase 20e). Nothing ranges over it, and the row carries "
                + "(OrganizationId, CreatedAt) for the audit list that does.",
        };

    /// <summary>
    /// The two master-data collections whose list ordering is not the column their per-tenant unique
    /// index already leads with, and whose row count NFR-5.1 sizes in tens of thousands.
    ///
    /// <para>Master data is otherwise covered for free: <c>(OrganizationId, Code)</c> or
    /// <c>(OrganizationId, Name)</c> exists on it because its uniqueness rule is per tenant. Contacts
    /// and Products are the exception in both halves at once — their unique index is on <c>Code</c>
    /// while both lists order by <c>Name</c>, so the tenant filter was a seek and the ordering was
    /// still a sort of every row the tenant owns (measured: 419&#160;ms p95 for the first page of
    /// 50,000 contacts, 42&#160;ms after). They are not an arbitrary two: <b>NFR-5.1 names contacts
    /// and products by name</b> as the collections that reach tens of thousands, and every other
    /// lookup a tenant keeps is bounded at tens or hundreds.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> MasterDataOrdering =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Contact"] = "Name",
            ["Product"] = "Name",
        };

    /// <summary>
    /// The columns phase 34b's list search matches on, in the order this convention builds a covering
    /// index from them. Every one of the twenty searchable list handlers matches some subset of these
    /// and nothing else — <c>Name</c> and <c>Code</c> for master data, <c>Code</c> plus a reference
    /// field for a document — which is what makes this derivable rather than a per-table decision.
    ///
    /// <para><b>Why a third index family, and why it is not optional.</b> A search term reaches
    /// <c>LIKE '%term%'</c>, which no index can turn into a seek. What an index can do is make the
    /// unavoidable scan narrow, and — this is the part that matters on a shared-schema database —
    /// make it a scan of <i>this tenant's</i> rows instead of the whole table. Measured on a
    /// non-matching term over 50,000 contacts with a second tenant of the same size present: 6,743
    /// logical reads against the clustered index, 700 against this one.</para>
    ///
    /// <para>Adding it was not optional because <b>this convention's own ordering index caused a
    /// regression without it</b>. Given <c>(OrganizationId, CreatedAt)</c> and nothing covering the
    /// search columns, the optimizer switched from one clustered scan to a seek plus a key lookup per
    /// row, and a term matching nothing went from 651&#160;ms to 1,069&#160;ms p95. The lesson is
    /// recorded in phase-34c-status.md: <i>an index added for one access path changes the plan for
    /// every other path over the same table.</i></para>
    /// </summary>
    private static readonly string[] SearchKeyOrder = ["Name", "Code"];

    /// <summary>
    /// Searched columns carried as INCLUDEs rather than keys: they are wide (200 characters) and
    /// never ordered on, so paying for them in the key would widen every level of the tree for no
    /// seek that anything performs.
    /// </summary>
    private static readonly string[] SearchIncludes = ["Reference", "SupplierInvoiceReference", "Sku"];

    /// <summary>
    /// Phase 42 -- columns the report-path index carries alongside the business date, where the
    /// entity has them.
    ///
    /// <para>Only <see cref="Domain.Accounting.GlJournalEntry"/> does, and that is the point: a GL
    /// entry stores nothing about its document but its type and its id, so <i>every</i> reader that
    /// ranges over posted entries projects exactly these two next to the date -- the Journal report,
    /// the Detail General Ledger, and phase 41's quota count. Without them the
    /// <c>(OrganizationId, PostedAt)</c> seek is followed by a key lookup per entry: measured at
    /// <b>184,718 logical reads and 766&#160;ms</b> for one Professional-ceiling quota count over a
    /// 70,002-entry term. They are declared as a rule over the columns rather than as an index on
    /// one table for the same reason <see cref="SearchIncludes"/> is -- a second entity that ever
    /// carries a source-document pointer gets the same treatment without anyone remembering.</para>
    ///
    /// <para>Phase 34c's lesson applies to this index as it did to the others: an index added for
    /// one access path changes the plan for every other path over the same table, so the passes on
    /// both sides of this change re-measure every GL reader, not only the quota count.</para>
    /// </summary>
    private static readonly string[] ReportPathIncludes = ["SourceDocumentType", "SourceDocumentId"];

    /// <summary>
    /// Tenant-scoped entities that deliberately carry no leading-OrganizationId index, each with the
    /// reason. Both are child rows that are never read by tenant: nothing lists them, and every
    /// query for them is already keyed by the parent they hang off.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NotIndexedByTenant =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CustomFieldValue"] =
                "Read only as the values of one document, by (ParentType, ParentId) -- the index that "
                + "already exists. No screen lists a tenant's custom-field values.",
            ["ImportJobRow"] =
                "The rows of one import job, read by ImportJobId. A tenant-wide list of import rows "
                + "is not a screen, and the rows are swept with their job.",
        };

    public static void Apply(ModelBuilder modelBuilder)
    {
        var unindexed = new List<string>();
        var unclassifiedDates = new List<string>();

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.IsOwned() || entity.FindProperty(OrganizationId) is null)
            {
                continue;
            }

            // Derived first, declared second: fourteen of seventeen spell it Date or PostedAt, and
            // the declaration exists for the ones that do not (phase 50).
            var businessDate = BusinessDateNames
                .Select(entity.FindProperty)
                .FirstOrDefault(p => p is not null);

            var listOrdersByCreatedAt = true;

            if (businessDate is null
                && DeclaredBusinessDates.TryGetValue(entity.ClrType.Name, out var declared))
            {
                businessDate = entity.FindProperty(declared.Column)
                    ?? throw new InvalidOperationException(
                        $"TenantIndexConvention declares {entity.ClrType.Name}.{declared.Column} as its "
                        + "business date, and the entity has no such property. The declaration and the "
                        + "aggregate have diverged -- one of them is wrong.");

                listOrdersByCreatedAt = declared.ListOrdersByCreatedAt;
            }

            if (businessDate is not null)
            {
                // Ascending for the fourteen, because their date index serves a range filter and the
                // ordering is CreatedAt's job. Descending for a document whose list orders by this
                // very column: the same declaration that says "no CreatedAt index" is what makes this
                // index the ordering index too, and the direction is then worth declaring. Measured
                // on the 50,000-cheque tenant, register first page, the two passes taken back to back
                // with statistics refreshed and the plan cache cleared between them: 1,450 logical
                // reads and 29.2 ms of CPU ascending, 1,286 and 24.4 ms descending. A modest win and
                // reported as one -- an earlier probe read it as nearly 2x, and that probe carried a
                // different plan-cache state, which is the difference between a measurement and a
                // number (phase 50).
                AddIndex(modelBuilder, entity, [OrganizationId, businessDate.Name],
                    descendingLast: !listOrdersByCreatedAt, includes: ReportPathIncludes);

                // The list path. A document without CreatedAt would be an aggregate no list screen
                // can order -- there is none today, and the null check keeps this a rule rather than
                // an assumption about all fourteen. A document whose list orders by its business date
                // instead (the Cheque Register) asks for no second index: the one above is already it.
                if (listOrdersByCreatedAt && entity.FindProperty(CreatedAt) is not null)
                {
                    AddIndex(modelBuilder, entity, [OrganizationId, CreatedAt], descendingLast: true);
                }

                AddSearchIndex(modelBuilder, entity);
                continue;
            }

            // The mirror question, asked of every entity the derivation just failed to classify:
            // does it carry a required date that nobody has decided about? "No business date found"
            // and "master data" used to be the same answer here, which is how a cheque's date went
            // unindexed for thirty-three phases (phase 50).
            if (RequiredDateProperty(entity) is { } strayDate
                && !DatesThatAreNotBusinessDates.ContainsKey(entity.ClrType.Name))
            {
                unclassifiedDates.Add($"{entity.ClrType.Name}.{strayDate}");
            }

            if (MasterDataOrdering.TryGetValue(entity.ClrType.Name, out var orderingColumn))
            {
                // The ordering index and the search index are one index here, because the column the
                // list orders by is also the first column it matches on: (OrganizationId, Name, Code)
                // is a range scan in Name order and covers `Name LIKE … OR Code LIKE …` outright.
                AddSearchIndex(modelBuilder, entity);

                if (!entity.GetIndexes().Any(i => i.Properties.Count >= 2
                        && i.Properties[0].Name == OrganizationId && i.Properties[1].Name == orderingColumn))
                {
                    AddIndex(modelBuilder, entity, [OrganizationId, orderingColumn], descendingLast: false);
                }

                continue;
            }

            if (NotIndexedByTenant.ContainsKey(entity.ClrType.Name) || HasLeadingTenantIndex(entity))
            {
                continue;
            }

            unindexed.Add(entity.ClrType.Name);
        }

        if (unclassifiedDates.Count > 0)
        {
            throw new InvalidOperationException(
                "These tenant-scoped entities carry a required date that this convention did not "
                + "recognise as a business date, so each was classified as master data by default and "
                + "silently given no (OrganizationId, <date>) index. Decide which it is: declare the "
                + "column in TenantIndexConvention.DeclaredBusinessDates if the entity is a document, "
                + "or name it in DatesThatAreNotBusinessDates with the reason its date is not one:\n  "
                + string.Join("\n  ", unclassifiedDates));
        }

        if (unindexed.Count > 0)
        {
            throw new InvalidOperationException(
                "These tenant-scoped entities carry no index whose leading key is OrganizationId, so "
                + "every query for one of them scans every tenant's rows (NFR-5.1). Give the entity a "
                + "per-tenant unique index, or add it to TenantIndexConvention.NotIndexedByTenant with "
                + "the reason it is only ever read through its parent:\n  "
                + string.Join("\n  ", unindexed));
        }
    }

    /// <summary>
    /// The covering index for phase 34b's list search — see <see cref="SearchKeyOrder"/> for why it
    /// is a separate family. Skipped for an entity that carries none of the searched columns, which
    /// is how a document with no <c>Code</c> would opt out without needing to be named here.
    /// </summary>
    private static void AddSearchIndex(ModelBuilder modelBuilder, IMutableEntityType entity)
    {
        var keys = SearchKeyOrder.Where(name => entity.FindProperty(name) is not null).ToArray();

        if (keys.Length == 0)
        {
            return;
        }

        string[] columns = [OrganizationId, .. keys];

        if (entity.GetIndexes().Any(i => i.Properties.Select(p => p.Name).SequenceEqual(columns)))
        {
            return;
        }

        var includes = SearchIncludes.Where(name => entity.FindProperty(name) is not null).ToArray();
        var index = modelBuilder.Entity(entity.ClrType).HasIndex(columns);

        if (includes.Length > 0)
        {
            index.IncludeProperties(includes);
        }
    }

    /// <summary>
    /// Adds the index unless an equivalent one is already configured, so a table that states its own
    /// <c>(OrganizationId, …)</c> index by hand — a unique one, say — is left exactly as it was.
    /// </summary>
    private static void AddIndex(
        ModelBuilder modelBuilder,
        IMutableEntityType entity,
        string[] columns,
        bool descendingLast,
        string[]? includes = null)
    {
        if (entity.GetIndexes().Any(i => i.Properties.Select(p => p.Name).SequenceEqual(columns)))
        {
            return;
        }

        var index = modelBuilder.Entity(entity.ClrType).HasIndex(columns);
        var present = (includes ?? []).Where(name => entity.FindProperty(name) is not null).ToArray();

        if (present.Length > 0)
        {
            index.IncludeProperties(present);
        }

        if (descendingLast)
        {
            // Newest first is what every document list asks for. SQL Server can scan an ascending
            // index backwards, so this is not strictly required -- it is declared because the
            // ordering is a fact about the screen, and reading the index tells you so.
            index.IsDescending(false, true);
        }
    }

    private static bool HasLeadingTenantIndex(IMutableEntityType entity) =>
        entity.GetIndexes().Any(i => i.Properties[0].Name == OrganizationId);

    /// <summary>
    /// The name of a required <see cref="DateOnly"/> property, if the entity has one.
    ///
    /// <para><c>DateOnly</c> and required, both deliberately. A business date in this codebase is
    /// always a <c>DateOnly</c> — <c>CreatedAt</c>, <c>PostedAt</c> as an instant, and every audit
    /// stamp are <c>DateTimeOffset</c>, so the type alone separates "the day this document happened"
    /// from "when the row was written". And required, because a nullable one is a secondary date a
    /// document carries beside its own: <c>Invoice.DueDate</c>, <c>Quotation.ExpiryDate</c>,
    /// <c>Cheque.ReceivedDate</c>. Neither half is a guess about names, which is the point.</para>
    /// </summary>
    private static string? RequiredDateProperty(IMutableEntityType entity) =>
        entity.GetProperties()
            .FirstOrDefault(p => p.ClrType == typeof(DateOnly) && !p.IsNullable)
            ?.Name;
}
