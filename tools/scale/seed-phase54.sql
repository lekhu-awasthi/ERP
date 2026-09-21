-- Phase 54 -- the rows the two outstanding measurement debts need, which the phase-34c dataset
-- deliberately does not have.
--
-- The 34c seed types its 20,001 products as **Service**, so no seeded document owes stock-ledger
-- rows (README, "Two seeding decisions shape what the numbers mean"). That is right for the GL
-- measurements and useless for either debt here:
--
--   * phase 51's filtered unique index lives on inventory.StockLedgerEntries, and the path it might
--     have disturbed is the FIFO walk over the same table -- 68 rows across every tenant on this
--     machine measures nothing;
--   * phase 52's .Include(SecondaryUnits) on ListProductsQueryHandler joins a child table holding
--     11 rows in total, so the join is free by accident rather than by design.
--
-- Both are seeded by direct INSERT, for the same reason the 190,000 bulk rows are: the shapes under
-- test are read shapes, and going through the API would take hours to build rows no assertion
-- reads. Nothing here is a document -- no GL, no movements -- so this tenant must NOT be used for a
-- conservation-law claim afterwards. It is a read-path fixture and says so.
--
-- Usage:
--   sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-phase54.sql \
--     -v OrgId="<guid>" NumLayers=200000 NumLayerProducts=2000 NumSecondaryUnits=20000
--
-- Re-runnable: it deletes its own rows first, identified by the sentinel SourceDocumentId below.

SET NOCOUNT ON;
-- Required to touch inventory.StockLedgerEntries at all: it carries a FILTERED index (phase 51's
-- serial uniqueness), and SQL Server refuses any DML on such a table unless QUOTED_IDENTIFIER is
-- ON. sqlcmd's default is OFF, which surfaces as a DELETE failing with msg 1934 and a message that
-- names five unrelated features.
SET QUOTED_IDENTIFIER ON;

DECLARE @Org uniqueidentifier = CONVERT(uniqueidentifier, '$(OrgId)');
DECLARE @Layers int = $(NumLayers);
DECLARE @Units int = $(NumSecondaryUnits);
-- How many distinct products the layers are spread over -- see the concentration note below.
DECLARE @Products int = $(NumLayerProducts);

-- Every row this script writes carries this SourceDocumentId, so a re-run is idempotent and the
-- fixture can be removed again without touching anything real.
DECLARE @Sentinel uniqueidentifier = CONVERT(uniqueidentifier, '54545454-5454-5454-5454-545454545454');

PRINT 'Clearing any previous phase-54 fixture rows...';
DELETE FROM inventory.StockLedgerEntries WHERE SourceDocumentId = @Sentinel;
DELETE FROM catalog.ProductSecondaryUnits
WHERE ProductId IN (SELECT Id FROM catalog.Products WHERE OrganizationId = @Org)
  AND ConversionRate IN (12, 24);

-- --------------------------------------------------------------------------------------------
-- 1. FIFO layers.
--
-- Concentrated over the first @Products of the tenant's 20,001 products, in its one warehouse,
-- dated across the range the 34c seed uses. Concentration is the point: the FIFO walk reads the
-- layers of ONE (product, warehouse), so what the walk costs depends on how deep that product's
-- stack is, while what a mistaken plan would cost depends on how big the table is. Spreading 200k
-- rows evenly over 20k products gives a 10-row walk against a big table and measures neither well.
--
-- One row in twenty carries a SerialNo, which is what puts content into the phase-51 filtered
-- unique index -- the index whose effect on the walk is the debt being paid here.
--
-- Two things about the join are load-bearing, both learned the slow way. The product for each row
-- comes from a #temp table with a clustered index on its key, not from a CTE; and the modulo is
-- MATERIALISED into a column of #n rather than written in the ON clause. A predicate of the form
-- `p.k = n.i % @Products` is not seekable, so SQL Server loops the product side once per row --
-- 200,000 x 2,000 comparisons, which burned ten CPU-minutes and had written 224 pages when it was
-- killed. Phase 50's lesson in another key: the plan is the thing, not the row count.
-- --------------------------------------------------------------------------------------------

DECLARE @Warehouse uniqueidentifier = (
    SELECT TOP 1 Id FROM tenancy.Warehouses WHERE OrganizationId = @Org ORDER BY Id);

IF @Warehouse IS NULL
BEGIN
    RAISERROR('No warehouse for that organization -- run seed-master.sh first.', 16, 1);
    RETURN;
END

PRINT 'Building the key tables...';

DROP TABLE IF EXISTS #p;
SELECT TOP (@Products) Id, CAST(ROW_NUMBER() OVER (ORDER BY Id) - 1 AS int) AS k
INTO #p
FROM catalog.Products WHERE OrganizationId = @Org ORDER BY Id;
CREATE UNIQUE CLUSTERED INDEX IX_p ON #p(k);

DECLARE @ProductsActual int = (SELECT COUNT(*) FROM #p);

DROP TABLE IF EXISTS #n;
SELECT i, CAST(i % @ProductsActual AS int) AS pk
INTO #n
FROM (
    SELECT TOP (@Layers) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS int) AS i
    FROM sys.all_columns a CROSS JOIN sys.all_columns b
) AS r;
CREATE CLUSTERED INDEX IX_n ON #n(pk);

PRINT 'Inserting FIFO layers...';

INSERT INTO inventory.StockLedgerEntries
    (Id, OrganizationId, ProductId, WarehouseId, SourceDocumentType, SourceDocumentId,
     QuantityIn, QuantityRemaining, UnitCost, TransactionDate, CreatedAt, LocationId, BatchId, SerialNo)
SELECT
    NEWID(), @Org, p.Id, @Warehouse, 'PurchaseBill', @Sentinel,
    100, 100, 10 + (n.i % 40),
    DATEADD(day, -(n.i % 900), CAST(GETDATE() AS date)),
    SYSDATETIMEOFFSET(),
    NULL, NULL,
    -- One in twenty serialised, and unique across the tenant: the filtered index is UNIQUE over
    -- (OrganizationId, ProductId, SerialNo) where QuantityRemaining > 0, and every row here has
    -- QuantityRemaining > 0, so a duplicate would fail the insert rather than skew the reading.
    CASE WHEN n.i % 20 = 0 THEN CONCAT('SN-', FORMAT(n.i, '0000000')) ELSE NULL END
FROM #n n
JOIN #p p ON p.k = n.pk;

DROP TABLE #n;
DROP TABLE #p;

DECLARE @LayerCount int = (SELECT COUNT(*) FROM inventory.StockLedgerEntries WHERE OrganizationId = @Org);
PRINT CONCAT('  layers now: ', @LayerCount);

-- --------------------------------------------------------------------------------------------
-- 2. Secondary units.
--
-- Two per product over the first @Units/2 products, which is the shape the reference tenant shows
-- (a product carries its primary plus a small handful). ListProductsQueryHandler's Include is a
-- LEFT JOIN over the page, so what matters to the reading is how many child rows a page drags
-- with it.
-- --------------------------------------------------------------------------------------------

PRINT 'Inserting product secondary units...';

-- The 34c seed gives the tenant exactly ONE unit (Piece), which is also every product's primary,
-- so there is nothing a secondary-unit row could legally name. Two are created here if they are
-- missing, by the short names the reference tenant uses.
IF NOT EXISTS (SELECT 1 FROM catalog.UnitsOfMeasurement WHERE OrganizationId = @Org AND ShortName = 'CTN')
    INSERT INTO catalog.UnitsOfMeasurement (Id, OrganizationId, Name, ShortName, IsActive, CreatedAt)
    VALUES (NEWID(), @Org, 'Carton', 'CTN', 1, SYSDATETIMEOFFSET());

IF NOT EXISTS (SELECT 1 FROM catalog.UnitsOfMeasurement WHERE OrganizationId = @Org AND ShortName = 'BOX')
    INSERT INTO catalog.UnitsOfMeasurement (Id, OrganizationId, Name, ShortName, IsActive, CreatedAt)
    VALUES (NEWID(), @Org, 'Box', 'BOX', 1, SYSDATETIMEOFFSET());

DECLARE @Unit uniqueidentifier = (
    SELECT Id FROM catalog.UnitsOfMeasurement WHERE OrganizationId = @Org AND ShortName = 'CTN');
DECLARE @Unit2 uniqueidentifier = (
    SELECT Id FROM catalog.UnitsOfMeasurement WHERE OrganizationId = @Org AND ShortName = 'BOX');

IF @Unit IS NULL OR @Unit2 IS NULL
BEGIN
    RAISERROR('Could not resolve the two secondary units.', 16, 1);
    RETURN;
END

;WITH p AS (
    SELECT TOP (@Units / 2) Id, PrimaryUnitId FROM catalog.Products WHERE OrganizationId = @Org ORDER BY Id
),
-- DISTINCT, because a tenant with a single unit of measurement collapses @Unit and @Unit2 to the
-- same id and the pair would then violate (ProductId, UnitId) within this one INSERT -- which the
-- NOT EXISTS below cannot see, since it only looks at rows that are already committed.
u AS (
    SELECT DISTINCT UnitId, Rate
    FROM (VALUES (@Unit, CAST(12 AS decimal(18,6))), (@Unit2, CAST(24 AS decimal(18,6)))) AS v(UnitId, Rate)
)
INSERT INTO catalog.ProductSecondaryUnits (Id, ProductId, UnitId, ConversionRate, SellingPrice, PurchasePrice)
SELECT NEWID(), p.Id, u.UnitId, u.Rate, 100, 80
FROM p CROSS JOIN u
-- The primary unit is not a secondary-unit row: Product.AddSecondaryUnit refuses it outright.
WHERE u.UnitId <> p.PrimaryUnitId
  AND NOT EXISTS (
      SELECT 1 FROM catalog.ProductSecondaryUnits x WHERE x.ProductId = p.Id AND x.UnitId = u.UnitId);

DECLARE @UnitCount int = (
    SELECT COUNT(*) FROM catalog.ProductSecondaryUnits s
    JOIN catalog.Products pr ON pr.Id = s.ProductId WHERE pr.OrganizationId = @Org);
PRINT CONCAT('  secondary units now: ', @UnitCount);

PRINT 'Done. Run refresh-stats.sql before measuring anything.';
