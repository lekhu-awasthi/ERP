-- Phase 57 -- the index debt phase 56 wrote into GlLineConfiguration, measured.
--
-- The claim under test: "the matcher's right-hand pane measured on tools/scale's 50k-invoice
-- dataset, on logical reads and never the wall clock" (phase 50's rule, because wall time on this
-- machine moves 3x-4x between two passes of an identical configuration).
--
-- Every statement below is the SQL EF actually issues, copied from the API's own log while driving
-- GET /bank-accounts/{id}/book-transactions against the 50k dataset -- not a hand-written
-- approximation of it. The account used is the busiest one in that tenant (50,001 lines); it is an
-- Accounts Receivable account rather than a Bank one, which the predicate cannot tell apart and
-- which is strictly worse than any bank account a real tenant would have.
--
-- Paths B, C and D are the paths this phase did NOT target, included because phase 34c's rule is
-- that an index added for one access path changes the plan for every other path on the same table,
-- and phase 50's refusal came from exactly that check.
--
-- Usage: sqlcmd -S <server> -d ErpApp -E -i tools/scale/probe-phase57-io.sql -v ORG="..." ACCT="..."

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Org uniqueidentifier = '$(ORG)';
DECLARE @Acct uniqueidentifier = '$(ACCT)';

SET STATISTICS IO ON;

PRINT '=== A. the matcher''s right-hand pane -- page 1 (reconciled = false) ===';
SELECT [g].[Id], [g].[GlJournalEntryId], [g].[Debit], [g].[Credit], [g].[ReconciliationId],
       [g1].[PostedAt], [g1].[SourceDocumentType], [g1].[SourceDocumentId]
FROM [accounting].[GlLines] AS [g]
INNER JOIN (
    SELECT [g0].[Id], [g0].[PostedAt], [g0].[SourceDocumentId], [g0].[SourceDocumentType]
    FROM [accounting].[GlJournalEntries] AS [g0]
    WHERE [g0].[OrganizationId] = @Org
) AS [g1] ON [g].[GlJournalEntryId] = [g1].[Id]
WHERE [g].[AccountId] = @Acct AND [g].[ReconciliationId] IS NULL
ORDER BY [g1].[PostedAt] DESC, [g].[Id]
OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY;

PRINT '';
PRINT '=== A2. the same pane''s count ===';
SELECT COUNT(*)
FROM [accounting].[GlLines] AS [g]
INNER JOIN (
    SELECT [g0].[Id] FROM [accounting].[GlJournalEntries] AS [g0]
    WHERE [g0].[OrganizationId] = @Org
) AS [g1] ON [g].[GlJournalEntryId] = [g1].[Id]
WHERE [g].[AccountId] = @Acct AND [g].[ReconciliationId] IS NULL;

PRINT '';
PRINT '=== B. Book Statement -- the same screen with no reconciled filter (NOT targeted) ===';
SELECT [g].[Id], [g].[GlJournalEntryId], [g].[Debit], [g].[Credit], [g].[ReconciliationId],
       [g1].[PostedAt], [g1].[SourceDocumentType], [g1].[SourceDocumentId]
FROM [accounting].[GlLines] AS [g]
INNER JOIN (
    SELECT [g0].[Id], [g0].[PostedAt], [g0].[SourceDocumentId], [g0].[SourceDocumentType]
    FROM [accounting].[GlJournalEntries] AS [g0]
    WHERE [g0].[OrganizationId] = @Org
) AS [g1] ON [g].[GlJournalEntryId] = [g1].[Id]
WHERE [g].[AccountId] = @Acct
ORDER BY [g1].[PostedAt] DESC, [g].[Id]
OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY;

PRINT '';
PRINT '=== C. the report balance -- a store-side sum over the account''s whole history (NOT targeted) ===';
SELECT SUM([g].[Debit] - [g].[Credit])
FROM [accounting].[GlLines] AS [g]
INNER JOIN (
    SELECT [g0].[Id] FROM [accounting].[GlJournalEntries] AS [g0]
    WHERE [g0].[OrganizationId] = @Org
) AS [g1] ON [g].[GlJournalEntryId] = [g1].[Id]
WHERE [g].[AccountId] = @Acct;

PRINT '';
PRINT '=== D. Trial Balance -- every account in the tenant (NOT targeted, reads the whole table) ===';
SELECT [g].[AccountId], SUM([g].[Debit]) AS D, SUM([g].[Credit]) AS C
FROM [accounting].[GlLines] AS [g]
INNER JOIN [accounting].[GlJournalEntries] AS [e] ON [e].[Id] = [g].[GlJournalEntryId]
WHERE [e].[OrganizationId] = @Org
GROUP BY [g].[AccountId];

SET STATISTICS IO OFF;
