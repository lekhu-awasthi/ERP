-- Phase 58 -- the physical balance's second half, which reads the ACCOUNTING ledger's table.
--
-- PhysicalStockReader sums inventory.StockMovements for the four shared types (StockBooks.Shared)
-- beside its own table. That read goes through IX_StockMovements_OrganizationId_ProductId_
-- WarehouseId_TransactionDate, an index this phase does not own, so the question is whether to
-- change it. Run against seed-phase58.sql's 200,000 accounting rows (one in four shared).
--
--   S. the shared half of the availability check -- the SQL EF issued, copied from the API log;
--   L. one product's history across the period, the shape the accounting ledger's own per-product
--      readers use (hand-written: the untargeted path, measured because phase 34c's rule is that an
--      index changed for one path moves every other path on the table).
--
-- Usage: sqlcmd -S <server> -d ErpApp -E -i probe-phase58-shared-io.sql -v ORG="..." PROD="..." WH="..."

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @organizationId uniqueidentifier = '$(ORG)';
DECLARE @ids1 uniqueidentifier = '$(PROD)';
DECLARE @warehouse uniqueidentifier = '$(WH)';
DECLARE @toDate date = CAST(SYSDATETIMEOFFSET() AS date);

SET STATISTICS IO ON;

PRINT '=== S. shared half of the availability check ===';
SELECT [s].[ProductId], COALESCE(SUM(CASE
    WHEN [s].[Direction] = N'In' THEN [s].[Quantity]
    ELSE -[s].[Quantity]
END), 0.0) AS [Quantity]
FROM [inventory].[StockMovements] AS [s]
WHERE [s].[OrganizationId] = @organizationId AND [s].[ProductId] = @ids1 AND [s].[SourceDocumentType] IN (N'WarehouseTransfer', N'InventoryAdjustment', N'ProductionJournal', N'OpeningStock') AND [s].[WarehouseId] = @warehouse
GROUP BY [s].[ProductId];

PRINT '=== R. tenant-wide shared load ===';
-- StockFactReader.LoadPhysicalMovementsAsync's second query (Position in Physical mode), from the log.
SELECT [s].[Id], [s].[ProductId], [s].[WarehouseId], [s].[TransactionDate], [s].[CreatedAt], [s].[SourceDocumentType], [s].[SourceDocumentId], [s].[Direction], [s].[Quantity]
INTO #r
FROM [inventory].[StockMovements] AS [s]
WHERE [s].[OrganizationId] = @organizationId AND [s].[TransactionDate] <= @toDate AND [s].[Quantity] <> 0.0 AND [s].[SourceDocumentType] IN (N'WarehouseTransfer', N'InventoryAdjustment', N'ProductionJournal', N'OpeningStock');

PRINT '=== L. one product history ===';
SELECT [s].[Id], [s].[ProductId], [s].[WarehouseId], [s].[Direction], [s].[Quantity], [s].[UnitCost], [s].[ValueAdjustment],
       [s].[SourceDocumentType], [s].[SourceDocumentId], [s].[TransactionDate], [s].[CreatedAt]
INTO #l
FROM [inventory].[StockMovements] AS [s]
WHERE [s].[OrganizationId] = @organizationId AND [s].[ProductId] = @ids1 AND [s].[TransactionDate] <= @toDate;

SET STATISTICS IO OFF;
