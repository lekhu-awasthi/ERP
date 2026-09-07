using ErpApp.Application.Common.Numbering;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence;

/// <summary>
/// Pessimistic-locking implementation of IDocumentNumberGenerator (architecture-spec.md §3.1).
/// Takes the concrete AppDbContext, not IAppDbContext, because it needs Database.SqlQuery/
/// ExecuteSqlInterpolatedAsync/BeginTransactionAsync, which IAppDbContext's DbSet-only surface
/// doesn't expose.
///
/// A single atomic "UPDATE ... OUTPUT" was the first approach tried here, but EF Core's
/// Database.SqlQuery&lt;T&gt; requires "composable" SQL (effectively a plain SELECT) since it
/// wraps the text as a FROM-subquery for the LINQ pipeline -- an UPDATE...OUTPUT statement is
/// rejected at translation time ("FromSql/SqlQuery was called with non-composable SQL"), caught
/// by this phase's own concurrency integration test failing against a real SQL Server. Replaced
/// with the classic explicit-transaction "SELECT ... WITH (UPDLOCK, ROWLOCK)" then "UPDATE"
/// pair architecture-spec.md §3.1 names as the alternative to a per-tenant-per-doctype SEQUENCE:
/// the UPDLOCK row lock, taken by the SELECT, is held for the rest of the transaction, so a
/// second concurrent caller's SELECT ... WITH (UPDLOCK) on the same row blocks until the first
/// caller commits (or rolls back) -- serializing access to that one (OrganizationId,
/// DocumentType) row without needing any client-side retry for the increment itself. This
/// bypasses EF's change tracker/RowVersion entirely for the increment (relying on
/// RowVersion-based optimistic concurrency here would allow the exact duplicate-number race the
/// spec warns against -- see DocumentNumberingRule's doc comment).
///
/// Row creation is lazy (first-ever call for an (OrganizationId, DocumentType) pair creates the
/// row with defaults) and race-safe via the unique index on (OrganizationId, DocumentType): a
/// concurrent loser's INSERT violates it and retries the whole SELECT/UPDATE path, by which point
/// the winner's row exists (and is visible once the winner's transaction has committed).
/// </summary>
public sealed class DocumentNumberGenerator(AppDbContext db) : IDocumentNumberGenerator
{
    private const int MaxLazyCreateAttempts = 3;
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<string> GetNextNumberAsync(
        Guid organizationId, DocumentType documentType, CancellationToken cancellationToken, Guid? locationId = null)
    {
        var documentTypeString = documentType.ToString();

        // Phase 32 -- resolve which counter this call draws from, and the prefix it formats with.
        //
        // The settings row (LocationId null) is the single source of Prefix and of the
        // LocationWiseNumbering flag for every location, so the flag is one decision per document
        // type rather than per branch, and a per-location counter can never drift onto a stale
        // prefix. Read outside the transaction below on purpose: it is settings, not the counter,
        // and taking a lock on it would serialize every location against every other one -- which is
        // precisely what location-wise numbering exists to avoid.
        var settings = await db.DocumentNumberingRules.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && r.DocumentType == documentType && r.LocationId == null)
            .Select(r => new { r.Prefix, r.LocationWiseNumbering })
            .SingleOrDefaultAsync(cancellationToken);

        // Null location, or the flag off, means the settings row is itself the counter -- byte for
        // byte the behaviour every tenant had before this phase.
        var counterLocationId = settings?.LocationWiseNumbering == true ? locationId : null;

        for (var attempt = 0; attempt < MaxLazyCreateAttempts; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // WITH (UPDLOCK, ROWLOCK) -- an exclusive-intent lock held for the rest of this
            // transaction, so a concurrent caller's identical SELECT on the same row blocks here
            // rather than both callers reading the same NextNumber. A zero result means no row
            // exists yet for this (org, docType) pair (NextNumber is always >= 1 for a real row).
            // Database.SqlQuery<TResult> for a scalar TResult requires the result set's single
            // column to be named "Value" -- that's the convention it maps against, not
            // positional binding, so NextNumber must be aliased.
            // Phase 32 -- the counter key gained LocationId, and a parameter cannot express
            // "= NULL", so the predicate is branched rather than parameterised. Both branches keep
            // the UPDLOCK/ROWLOCK shape unchanged; the only difference is which single row it locks.
            var currentNumber = await db.Database.SqlQuery<int>(
                counterLocationId is null
                    ? (FormattableString)$"""
                       SELECT NextNumber AS Value FROM configuration.DocumentNumberingRules WITH (UPDLOCK, ROWLOCK)
                       WHERE OrganizationId = {organizationId} AND DocumentType = {documentTypeString}
                         AND LocationId IS NULL
                       """
                    : $"""
                       SELECT NextNumber AS Value FROM configuration.DocumentNumberingRules WITH (UPDLOCK, ROWLOCK)
                       WHERE OrganizationId = {organizationId} AND DocumentType = {documentTypeString}
                         AND LocationId = {counterLocationId}
                       """)
                .SingleOrDefaultAsync(cancellationToken);

            if (currentNumber != 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    counterLocationId is null
                        ? (FormattableString)$"""
                           UPDATE configuration.DocumentNumberingRules SET NextNumber = NextNumber + 1
                           WHERE OrganizationId = {organizationId} AND DocumentType = {documentTypeString}
                             AND LocationId IS NULL
                           """
                        : $"""
                           UPDATE configuration.DocumentNumberingRules SET NextNumber = NextNumber + 1
                           WHERE OrganizationId = {organizationId} AND DocumentType = {documentTypeString}
                             AND LocationId = {counterLocationId}
                           """,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // The prefix always comes from the settings row, read once above -- never from the
                // per-location row, whose settings columns are inert (see DocumentNumberingRule.LocationId).
                return FormatNumber(settings?.Prefix ?? string.Empty, currentNumber);
            }

            var isLastAttempt = attempt == MaxLazyCreateAttempts - 1;

            try
            {
                // Reserves number 1 for this call -- NextNumber starts at 2 so the very next
                // caller's UPDATE (above) hands out 2.
                //
                // A per-location row is created with an empty Prefix and all flags false, and that
                // is deliberate rather than lazy: those columns are never read on such a row, and
                // copying the settings row's values here would create a second, staler copy that a
                // later reader could believe.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO configuration.DocumentNumberingRules
                         (Id, OrganizationId, DocumentType, LocationId, Prefix, NextNumber, Mode,
                          ResetEveryFiscalYear, IncludeFiscalYearInCode, LocationWiseNumbering, CreatedAt)
                     VALUES
                         ({Guid.NewGuid()}, {organizationId}, {documentTypeString}, {counterLocationId}, {""}, 2,
                          {nameof(NumberingMode.Auto)}, {false}, {false}, {false}, {DateTimeOffset.UtcNow})
                     """,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return FormatNumber(settings?.Prefix ?? string.Empty, 1);
            }
            catch (SqlException ex) when (
                (ex.Number == UniqueIndexViolation || ex.Number == UniqueConstraintViolation) && !isLastAttempt)
            {
                // Lost the create race to a concurrent caller -- roll back this transaction (so
                // its UPDLOCK, if any was somehow held, is released) and retry; the winner's row
                // will be visible once its transaction has committed.
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        throw new InvalidOperationException(
            $"Could not assign a document number for {documentType} after {MaxLazyCreateAttempts} attempts.");
    }

    private static string FormatNumber(string prefix, int number) => $"{prefix}{number:D4}";
}
