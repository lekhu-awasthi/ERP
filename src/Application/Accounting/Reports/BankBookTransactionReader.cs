using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Reports;

/// <summary>
/// Phase 56 — the one reader every screen in the reconciliation module uses for <b>this tenant's own
/// side</b>: the movements of money through one cash-and-bank account, as recorded by this tenant's
/// documents rather than by the bank.
///
/// <para><b>It reads <see cref="GlLine"/>, and that is the phase's central decision.</b> The
/// reference product's matcher, its Book Statement screen and its report all call one endpoint,
/// <c>/gl-transactions</c>, whose row is one GL posting against the account carrying its source
/// document's code and reference (read live, 2026-09-21; its other feed, <c>/transactions</c>, is
/// document-level and is used by none of the six screens). Arriving at the same answer from this
/// codebase's own rules: what a bank statement line corresponds to is <i>one movement of money into
/// or out of the account</i>, and phase 36 established that a document can post more than one entry
/// while a document may touch the bank account twice or not at all. Only the line is one
/// movement.</para>
///
/// <para><b>What that costs, stated rather than discovered later.</b> <c>GlJournalEntry</c> stores
/// no copy of its document's number, reference or business date — phase 26a — so:
/// <list type="number">
/// <item>the code and reference need a join back across the thirteen GL-posting types, which is
/// <see cref="GlSourceDocumentResolver"/>'s job and is why this reader exists rather than each
/// screen writing it again;</item>
/// <item><b>the date is the posting date</b>, <c>PostedAt</c>, not the document's own. The reference
/// product shows the business date because its GL row denormalises one. Ours cannot, and this is
/// not a new divergence: the Journal report, the Detail General Ledger and the GL Master Report all
/// already show and filter on <c>PostedAt</c>, and phase 26a's rule is that a report must show the
/// same date field it filters on — which this does. Every screen here labels it <i>Posted</i>.</item>
/// </list>
/// </para>
///
/// <para><b>One reader, so the matcher and the report cannot disagree.</b> Phase 36's rule: two
/// reports agree only through one shared reader plus a test reading both on the same data. The
/// report's <i>Unreconciled</i> figures are the sums of exactly the rows the matcher's right-hand
/// pane offers, and they are that by construction rather than by coincidence.</para>
///
/// <para><b>Why this hands back answers and not an <c>IQueryable</c>.</b> It used to expose the
/// joined query already projected into <see cref="BookMovement"/>, and every handler then ordered,
/// counted and summed over that. <b>It 500s on SQL Server</b> — EF cannot translate
/// <c>OrderBy(x =&gt; new BookMovement(…).PostedAt)</c>, because the ordering key is a member of a
/// record the client would construct — while InMemory evaluates the whole thing in C# and every
/// handler test passes. That is the same door phase 25 found with a captured <c>Func</c>, phase 34b
/// with a static call and phase 42 with a <c>Sum</c> over an already-projected record, walked
/// through a fourth time; only the manual E2E saw it. So the projection is now the <i>last</i>
/// operation in every path here, ordering and paging happen on the entities' own columns, and there
/// is no shape a caller can compose that reintroduces it.</para>
/// </summary>
public sealed class BankBookTransactionReader(IAppDbContext db)
{
    /// <summary>
    /// How many movements match. Answered by the store, and the caller is expected to stop here when
    /// it returns zero — a count of zero is a complete answer and the page query after it is pure
    /// cost (phase 42).
    /// </summary>
    public Task<int> CountAsync(BookMovementFilter filter, CancellationToken cancellationToken) =>
        Filtered(filter).CountAsync(cancellationToken);

    /// <summary>
    /// The net of the matching movements, signed from the account's point of view.
    ///
    /// <para>Summed <b>by the store</b> over two real columns, never by loading the rows: a balance
    /// is cumulative, so the set behind it is the account's whole history up to a date, and pulling
    /// that into memory would make the report linear in the history rather than in the page (phase
    /// 42's rule about the Detail General Ledger).</para>
    /// </summary>
    public async Task<BankMovementTotal> SumAsync(
        BookMovementFilter filter, CancellationToken cancellationToken) =>
        BankMovementTotal.FromGlSum(
            await Filtered(filter).SumAsync(x => x.Line.Debit - x.Line.Credit, cancellationToken));

    /// <summary>
    /// One page of movements, newest posting first — the reference product's own default
    /// (<c>sorts=-date</c>) and the only ordering these screens offer (see
    /// <c>ListBookTransactionsQuery</c> for why there is no menu). The <c>GlLine</c> id breaks the
    /// tie, so the page boundary is stable when several lines share one <c>PostedAt</c>, which every
    /// multi-line entry does by construction.
    /// </summary>
    public async Task<IReadOnlyList<BookMovement>> PageAsync(
        BookMovementFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        // Ordered and paged on the entities' own columns; projected only after Skip/Take, which is
        // both what SQL Server can translate and what stops the page fetching whole rows it will
        // throw away (phase 42).
        var rows = await Filtered(filter)
            .OrderByDescending(x => x.Entry.PostedAt)
            .ThenBy(x => x.Line.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Line.Id,
                x.Line.GlJournalEntryId,
                x.Line.Debit,
                x.Line.Credit,
                x.Line.ReconciliationId,
                x.Entry.PostedAt,
                x.Entry.SourceDocumentType,
                x.Entry.SourceDocumentId,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(x => new BookMovement(
                x.Id, x.GlJournalEntryId, x.Debit, x.Credit, x.ReconciliationId,
                x.PostedAt, x.SourceDocumentType, x.SourceDocumentId)),
        ];
    }

    /// <summary>
    /// Every movement in one reconciliation, oldest first. Not paged, for
    /// <c>GetBankReconciliationQuery</c>'s reason: a reconciliation's size is bounded by what one
    /// person ticked in one pass.
    /// </summary>
    public async Task<IReadOnlyList<BookMovement>> ForReconciliationAsync(
        Guid organizationId, Guid bankAccountId, Guid reconciliationId, CancellationToken cancellationToken)
    {
        var rows = await Filtered(new BookMovementFilter(organizationId, bankAccountId))
            .Where(x => x.Line.ReconciliationId == reconciliationId)
            .OrderBy(x => x.Entry.PostedAt)
            .ThenBy(x => x.Line.Id)
            .Select(x => new
            {
                x.Line.Id,
                x.Line.GlJournalEntryId,
                x.Line.Debit,
                x.Line.Credit,
                x.Line.ReconciliationId,
                x.Entry.PostedAt,
                x.Entry.SourceDocumentType,
                x.Entry.SourceDocumentId,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(x => new BookMovement(
                x.Id, x.GlJournalEntryId, x.Debit, x.Credit, x.ReconciliationId,
                x.PostedAt, x.SourceDocumentType, x.SourceDocumentId)),
        ];
    }

    /// <summary>
    /// Phase 57 — the net movement per day over the filter's window, for the Balance History chart.
    ///
    /// <para><b>Bucketed in memory, deliberately.</b> The group key is the posting instant's day,
    /// and the only forms of that expression the store could translate would either pin the chart to
    /// the server's calendar in a way <c>GlDateBoundary</c> does not, or be untranslatable outright —
    /// this reader's own doc comment records what a projection EF cannot see through costs here.
    /// Two columns and a timestamp come back, one row per movement, and the bucketing is a
    /// <c>GroupBy</c> in C#.</para>
    ///
    /// <para><b>The cost is in the window, not the history</b>, which is the point: the balance
    /// <i>before</i> the window is one store-side <see cref="SumAsync"/>, so this loads only the
    /// movements the chart actually draws. That is the inverse of the trap phase 42 named on the
    /// Detail General Ledger, where the opening balance made a one-month report slower than a
    /// three-year one.</para>
    /// </summary>
    public async Task<IReadOnlyList<DailyBookMovement>> DailyNetAsync(
        BookMovementFilter filter, CancellationToken cancellationToken)
    {
        var rows = await Filtered(filter)
            .Select(x => new { x.Entry.PostedAt, x.Line.Debit, x.Line.Credit })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .GroupBy(x => DateOnly.FromDateTime(x.PostedAt.UtcDateTime))
                .Select(g => new DailyBookMovement(g.Key, g.Sum(x => x.Debit - x.Credit)))
                .OrderBy(x => x.Day),
        ];
    }

    /// <summary>
    /// Turns a page of movements into rows a screen can render: the source document's code and
    /// reference, and the contra accounts the money came from or went to.
    ///
    /// <para>Called with a <b>page</b>, never a period. The document join is one batched round trip
    /// per document type present on the page and is skipped for types that are not
    /// (<see cref="GlSourceDocumentResolver"/>), which is only linear in the page because the
    /// caller has already paged — phase 34c's rule about a report that loads its period and then
    /// re-queries children by <c>ids.Contains</c>.</para>
    /// </summary>
    public async Task<IReadOnlyList<BookTransactionDto>> DescribeAsync(
        Guid organizationId,
        Guid bankAccountId,
        IReadOnlyList<BookMovement> page,
        CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var documents = await GlSourceDocumentResolver.LoadAsync(
            db,
            organizationId,
            [.. page.Select(x => (x.SourceDocumentType, x.SourceDocumentId)).Distinct()],
            cancellationToken);

        // The contra side needs every line of each entry the page touches, including lines against
        // accounts the page never shows -- DetailGeneralLedgerQueryHandler's Description column,
        // built the same way and for the same reason.
        var entryIds = page.Select(x => x.GlJournalEntryId).Distinct().ToList();

        var siblings = await db.GlLines
            .Where(x => entryIds.Contains(x.GlJournalEntryId) && x.AccountId != bankAccountId)
            .Select(x => new { x.GlJournalEntryId, x.AccountId })
            .ToListAsync(cancellationToken);

        var contraAccountIds = siblings.Select(x => x.AccountId).Distinct().ToList();

        var accountNames = await db.Accounts
            .Where(x => x.OrganizationId == organizationId && contraAccountIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var nameById = accountNames.ToDictionary(x => x.Id, x => x.Name);

        var contraByEntry = siblings
            .GroupBy(x => x.GlJournalEntryId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(
                    ", ",
                    g.Select(x => nameById.GetValueOrDefault(x.AccountId))
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct()));

        return
        [
            .. page.Select(movement =>
            {
                var document = documents.For(movement.SourceDocumentType, movement.SourceDocumentId);
                var contra = contraByEntry.GetValueOrDefault(movement.GlJournalEntryId);

                return new BookTransactionDto(
                    movement.GlLineId,
                    DateOnly.FromDateTime(movement.PostedAt.UtcDateTime),
                    movement.PostedAt,
                    movement.SourceDocumentType,
                    movement.SourceDocumentId,
                    document?.Code,
                    document?.Reference,
                    string.IsNullOrWhiteSpace(contra) ? null : contra,
                    movement.Debit,
                    movement.Credit,
                    movement.Debit - movement.Credit,
                    movement.ReconciliationId);
            }),
        ];
    }

    /// <summary>
    /// The join, with every filter applied and <b>nothing projected</b>. Both halves of the pair stay
    /// as entities so that ordering, counting and summing all happen against real columns.
    ///
    /// <para>The tenant filter is on the <i>entry</i> because that is where <c>OrganizationId</c>
    /// lives — <see cref="GlLine"/> is a child row with no tenant column — and the organization is
    /// taken as an argument rather than read from anywhere ambient, which is phase 35b's rule for a
    /// shared helper that replaces a per-handler <c>Where</c>: it must own every condition that
    /// <c>Where</c> carried.</para>
    /// </summary>
    private IQueryable<GlMovementRow> Filtered(BookMovementFilter filter)
    {
        var query = db.GlLines
            .Where(line => line.AccountId == filter.BankAccountId)
            .Join(
                db.GlJournalEntries.Where(entry => entry.OrganizationId == filter.OrganizationId),
                line => line.GlJournalEntryId,
                entry => entry.Id,
                (line, entry) => new GlMovementRow { Line = line, Entry = entry });

        // Composed as separate Where clauses, never folded into one predicate with a null check: an
        // expression tree does not short-circuit, so `!flag || ...` hands EF the null branch to
        // translate anyway (phase 33's gotcha, and phase 35a's correction that a captured bool is
        // the case that really bites).
        if (filter.Reconciled is { } wantReconciled)
        {
            query = wantReconciled
                ? query.Where(x => x.Line.ReconciliationId != null)
                : query.Where(x => x.Line.ReconciliationId == null);
        }

        if (filter.FromDate is { } from)
        {
            query = query.Where(x => x.Entry.PostedAt >= GlDateBoundary.StartOfDayUtc(from));
        }

        if (filter.ToDate is { } to)
        {
            query = query.Where(x => x.Entry.PostedAt <= GlDateBoundary.EndOfDayUtc(to));
        }

        return query;
    }

    /// <summary>
    /// The joined pair, as a mutable class with two entity-typed properties rather than a positional
    /// record. <b>Both halves of that sentence are load-bearing:</b> EF sees through property access
    /// on it (<c>x.Entry.PostedAt</c> is a column), while a record's constructor in an intermediate
    /// operator is what it could not translate.
    /// </summary>
    private sealed class GlMovementRow
    {
        public GlLine Line { get; init; } = null!;

        public GlJournalEntry Entry { get; init; } = null!;
    }
}

/// <param name="Reconciled">Null shows everything; false is the matcher's pane and the report's
/// <i>Unreconciled</i> section.</param>
public sealed record BookMovementFilter(
    Guid OrganizationId,
    Guid BankAccountId,
    bool? Reconciled = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null);

/// <summary>
/// Phase 57 — one day's net movement through the account, signed the account's way. The
/// <see cref="Day"/> is the <b>UTC</b> day of the posting instant, matching what
/// <c>GlDateBoundary</c> filters on and what <see cref="BookTransactionDto.Date"/> shows; see
/// <c>BankBalanceHistoryQuery</c> for why the chart follows the ledger's calendar rather than
/// Kathmandu's.
/// </summary>
public sealed record DailyBookMovement(DateOnly Day, decimal Signed);

/// <summary>
/// One GL posting against the bank account, materialised. Built only after <c>ToListAsync</c> — see
/// the reader's last paragraph for the 500 that taught this.
/// </summary>
public sealed record BookMovement(
    Guid GlLineId,
    Guid GlJournalEntryId,
    decimal Debit,
    decimal Credit,
    Guid? ReconciliationId,
    DateTimeOffset PostedAt,
    DocumentType SourceDocumentType,
    Guid SourceDocumentId);

/// <param name="Date">The <b>posting</b> date, and every template labels it so — see the reader's
/// doc comment for why this codebase has no other date to offer here.</param>
/// <param name="SignedAmount">Money into the account is positive. This is
/// <c>BankMovementTotal.OfGlLine</c>'s value, carried as a decimal because a DTO crosses the wire;
/// the typed form is what the matcher's rule is enforced in.</param>
/// <param name="Description">The other accounts the same journal entry touched.</param>
public sealed record BookTransactionDto(
    Guid Id,
    DateOnly Date,
    DateTimeOffset PostedAt,
    DocumentType DocumentType,
    Guid DocumentId,
    string? DocumentCode,
    string? Reference,
    string? Description,
    decimal Debit,
    decimal Credit,
    decimal SignedAmount,
    Guid? ReconciliationId);
