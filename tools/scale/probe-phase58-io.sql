-- Phase 58 -- the three read paths over inventory.PhysicalStockMovements, on logical reads.
--
-- Every statement is the SQL EF issues, copied from the API's own log while driving the physical
-- ledger (phase 57's rule: not a hand-written approximation). Run against seed-phase58.sql's
-- 200,000-row fixture; run-probe-phase58.py drives it once per index variant, because an index added
-- for one path changes the plan for every other path on the table (phase 34c).
--
--   A. the report load -- StockFactReader.LoadPhysicalMovementsAsync (Inventory Position in
--      Physical mode, the Variance report's Actual column): the tenant's rows up to a date;
--   B. the availability check -- PhysicalStockReader.GetOnHandAsync, which a Delivery Note's
--      Approve and a Goods Received Note's Void both run: one product's balance in one warehouse;
--   C. the void lookup -- PhysicalStockWriter.ReverseAsync: one source document's own rows.
--
-- Usage: sqlcmd -S <server> -d ErpApp -E -i probe-phase58-io.sql -v ORG="..." PROD="..." WH="..." DOC="..."

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @organizationId uniqueidentifier = '$(ORG)';
DECLARE @ids1 uniqueidentifier = '$(PROD)';
DECLARE @warehouse uniqueidentifier = '$(WH)';
DECLARE @doc uniqueidentifier = '$(DOC)';
DECLARE @toDate date = CAST(SYSDATETIMEOFFSET() AS date);


SET STATISTICS IO ON;

PRINT '=== A. report load ===';
-- Materialised INTO a temp table, never wrapped in COUNT(*): a count lets the optimiser drop the
-- unread columns and answer from the narrowest index, which is not the query EF sends (the first
-- run of this probe measured exactly that, 1,839 reads against a real 4,584).
SELECT [p].[Id], [p].[ProductId], [p].[WarehouseId], [p].[TransactionDate], [p].[CreatedAt], [p].[SourceDocumentType], [p].[SourceDocumentId], [p].[Direction], [p].[Quantity]
INTO #a
FROM [inventory].[PhysicalStockMovements] AS [p]
WHERE [p].[OrganizationId] = @organizationId AND [p].[TransactionDate] <= @toDate;

PRINT '=== B. availability check ===';
SELECT [p].[ProductId], COALESCE(SUM(CASE
    WHEN [p].[Direction] = N'In' THEN [p].[Quantity]
    ELSE -[p].[Quantity]
END), 0.0) AS [Quantity]
FROM [inventory].[PhysicalStockMovements] AS [p]
WHERE [p].[OrganizationId] = @organizationId AND [p].[ProductId] = @ids1 AND [p].[WarehouseId] = @warehouse
GROUP BY [p].[ProductId];

PRINT '=== C. void lookup ===';
SELECT [p].[Id], [p].[CreatedAt], [p].[Direction], [p].[LocationId], [p].[OrganizationId], [p].[ProductId], [p].[Quantity], [p].[SourceDocumentId], [p].[SourceDocumentType], [p].[TransactionDate], [p].[WarehouseId]
FROM [inventory].[PhysicalStockMovements] AS [p]
WHERE [p].[OrganizationId] = @organizationId AND [p].[SourceDocumentType] = N'DeliveryNote' AND [p].[SourceDocumentId] = @doc;

SET STATISTICS IO OFF;
