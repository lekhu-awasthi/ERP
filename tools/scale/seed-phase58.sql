-- Phase 58 -- the rows the physical ledger's index decisions need, which no earlier dataset has.
--
-- inventory.PhysicalStockMovements is new, so every tenant on this machine holds a handful of rows
-- and any plan over it measures nothing. This seeds the 34c tenant with a physical ledger of the
-- same order as phase 54's 200,000 FIFO layers: NumRows rows over NumProducts products and one
-- warehouse, two rows per source document (alternating Goods Received Note In / Delivery Note Out),
-- dated across the past year.
--
-- Direct INSERT, for the reason phase 34c and 54 give: the shapes under test are read shapes.
-- Nothing here is a document, so this tenant must not be used for a conservation-law claim; it is
-- a read-path fixture and says so.
--
-- Usage:
--   sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-phase58.sql \
--     -v OrgId="<guid>" WarehouseId="<guid>" NumRows=200000 NumProducts=2000
--
-- Re-runnable: every row carries the sentinel CreatedAt below, and a re-run deletes those first.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Org uniqueidentifier = CONVERT(uniqueidentifier, '$(OrgId)');
DECLARE @Wh uniqueidentifier = CONVERT(uniqueidentifier, '$(WarehouseId)');
DECLARE @Rows int = $(NumRows);
DECLARE @Products int = $(NumProducts);
DECLARE @Sentinel datetimeoffset = '2000-01-01T00:00:00+00:00';
DECLARE @Today date = CAST(SYSDATETIMEOFFSET() AS date);

DELETE FROM inventory.PhysicalStockMovements WHERE OrganizationId = @Org AND CreatedAt = @Sentinel;

-- Materialised keys, never a modulo in a join's ON clause: phase 54's seed looped the other side
-- per row for ten CPU-minutes over exactly that shape.
CREATE TABLE #p (k int PRIMARY KEY, Id uniqueidentifier NOT NULL);
INSERT INTO #p (k, Id)
SELECT ROW_NUMBER() OVER (ORDER BY Id) - 1, Id
FROM catalog.Products
WHERE OrganizationId = @Org
ORDER BY Id
OFFSET 0 ROWS FETCH NEXT @Products ROWS ONLY;

CREATE TABLE #d (k int PRIMARY KEY, Id uniqueidentifier NOT NULL DEFAULT NEWID());
INSERT INTO #d (k)
SELECT TOP (@Rows / 2) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1
FROM sys.all_objects a CROSS JOIN sys.all_objects b;

CREATE TABLE #n (i int PRIMARY KEY, pk int NOT NULL, dk int NOT NULL);
INSERT INTO #n (i, pk, dk)
SELECT i, i % @Products, i / 2
FROM (SELECT TOP (@Rows) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i
      FROM sys.all_objects a CROSS JOIN sys.all_objects b) AS x;

INSERT INTO inventory.PhysicalStockMovements
    (Id, OrganizationId, ProductId, WarehouseId, Direction, Quantity, SourceDocumentType, SourceDocumentId,
     TransactionDate, CreatedAt, LocationId)
SELECT NEWID(), @Org, p.Id, @Wh,
       CASE WHEN n.dk % 2 = 0 THEN N'In' ELSE N'Out' END,
       1 + n.i % 5,
       CASE WHEN n.dk % 2 = 0 THEN N'GoodsReceivedNote' ELSE N'DeliveryNote' END,
       d.Id,
       DATEADD(day, -(n.dk % 365), @Today),
       @Sentinel,
       NULL
FROM #n AS n
JOIN #p AS p ON p.k = n.pk
JOIN #d AS d ON d.k = n.dk;

SELECT COUNT(*) AS SeededRows FROM inventory.PhysicalStockMovements WHERE OrganizationId = @Org AND CreatedAt = @Sentinel;

UPDATE STATISTICS inventory.PhysicalStockMovements WITH FULLSCAN;

-- The same number of accounting-ledger movements, because the physical balance's second half reads
-- inventory.StockMovements for the four shared types (StockBooks.Shared) and the 34c tenant has
-- none. One row in four is a shared type (Opening Stock / Inventory Adjustment); the rest are
-- Invoice / Purchase Bill, which that read must skip. Same sentinel, same products, same warehouse.
DELETE FROM inventory.StockMovements WHERE OrganizationId = @Org AND CreatedAt = @Sentinel;

INSERT INTO inventory.StockMovements
    (Id, OrganizationId, ProductId, WarehouseId, Direction, Quantity, UnitCost, SourceDocumentType, SourceDocumentId,
     TransactionDate, CreatedAt, LocationId, ValueAdjustment, BatchId, SerialNo)
SELECT NEWID(), @Org, p.Id, @Wh,
       CASE WHEN n.i % 2 = 0 THEN N'In' ELSE N'Out' END,
       1 + n.i % 5,
       100,
       CASE n.i % 4 WHEN 0 THEN N'OpeningStock' WHEN 1 THEN N'Invoice' WHEN 2 THEN N'PurchaseBill' ELSE N'Invoice' END,
       d.Id,
       DATEADD(day, -(n.dk % 365), @Today),
       @Sentinel,
       NULL, 0, NULL, NULL
FROM #n AS n
JOIN #p AS p ON p.k = n.pk
JOIN #d AS d ON d.k = n.dk;

SELECT COUNT(*) AS SeededAccountingRows FROM inventory.StockMovements WHERE OrganizationId = @Org AND CreatedAt = @Sentinel;

UPDATE STATISTICS inventory.StockMovements WITH FULLSCAN;
