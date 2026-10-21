-- Phase 95 -- a barcode on every product, for measuring the till's scan (phase 62 § 5: the barcode lookup is unindexed).
-- The phase 34c tenant's 20,001 products carry no barcode, so a scan there is a miss for the wrong reason. Each gets a
-- distinct 13-digit code derived from its id, as a shop that labels its whole shelf would have.
--
-- Usage:
--   sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-phase95.sql -v OrgId="<guid>"

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Org uniqueidentifier = '$(OrgId)';

UPDATE catalog.Products
SET Barcode = CONCAT('890', RIGHT(CONCAT('0000000000', ABS(CHECKSUM(Id)) % 10000000000), 10))
WHERE OrganizationId = @Org AND Barcode IS NULL;

SELECT COUNT(*) AS WithBarcode, COUNT(DISTINCT Barcode) AS DistinctBarcodes
FROM catalog.Products
WHERE OrganizationId = @Org AND Barcode IS NOT NULL;
