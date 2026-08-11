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

    public async Task<string> GetNextNumberAsync(Guid organizationId, DocumentType documentType, CancellationToken cancellationToken)
    {
        var documentTypeString = documentType.ToString();

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
            var currentNumber = await db.Database.SqlQuery<int>(
                $"""
                 SELECT NextNumber AS Value FROM configuration.DocumentNumberingRules WITH (UPDLOCK, ROWLOCK)
                 WHERE OrganizationId = {organizationId} AND DocumentType = {documentTypeString}
                 """)
                .SingleOrDefaultAsync(cancellationToken);

            if (currentNumber != 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE configuration.DocumentNumberingRules SET NextNumber = NextNumber + 1
                     WHERE OrganizationId = {organizationId} AND DocumentType = {documentTypeString}
                     """,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var prefix = await db.DocumentNumberingRules.AsNoTracking()
                    .Where(r => r.OrganizationId == organizationId && r.DocumentType == documentType)
                    .Select(r => r.Prefix)
                    .SingleAsync(cancellationToken);

                return FormatNumber(prefix, currentNumber);
            }

            var isLastAttempt = attempt == MaxLazyCreateAttempts - 1;

            try
            {
                // Reserves number 1 for this call -- NextNumber starts at 2 so the very next
                // caller's UPDATE (above) hands out 2.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO configuration.DocumentNumberingRules
                         (Id, OrganizationId, DocumentType, Prefix, NextNumber, Mode,
                          ResetEveryFiscalYear, IncludeFiscalYearInCode, LocationWiseNumbering, CreatedAt)
                     VALUES
                         ({Guid.NewGuid()}, {organizationId}, {documentTypeString}, {""}, 2, {nameof(NumberingMode.Auto)},
                          {false}, {false}, {false}, {DateTimeOffset.UtcNow})
                     """,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return FormatNumber(string.Empty, 1);
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
