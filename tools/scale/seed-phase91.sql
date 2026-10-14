-- Phase 91 -- a till-heavy tenant: the walk-in owns every counter sale, which is the one contact phase 90's
-- narrowing by contact cannot help (phase-90-status.md § 5). On the phase 42/50/90 tenant after seed-phase90.sql:
--   * the 20,000 invoices seed-phase90.sql paid by a till tender become till sales (Channel Pos) of the
--     walk-in, at the head office, which becomes a Retail till selling from the tenant's warehouse;
--   * NumRefunds of them get an approved till refund (a credit note naming its sale, one line copying the
--     sale's first line, paid out in cash) -- the credit-note half of what the walk-in's settlements read.
-- No GL is written and no stock moves: the reader under measurement reads documents, never the ledger.
--
-- Usage:
--   sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-phase91.sql -v OrgId="<guid>" NumRefunds=2000

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

DECLARE @Org uniqueidentifier = '$(OrgId)';
DECLARE @NumRefunds int = $(NumRefunds);

DECLARE @WalkIn uniqueidentifier =
    (SELECT TOP 1 Id FROM contacts.Contacts WHERE OrganizationId = @Org AND IsWalkInCustomer = 1);
DECLARE @Warehouse uniqueidentifier =
    (SELECT TOP 1 Id FROM tenancy.Warehouses WHERE OrganizationId = @Org ORDER BY Name);
DECLARE @Location uniqueidentifier =
    (SELECT TOP 1 Id FROM tenancy.BillingLocations WHERE OrganizationId = @Org AND Code = N'HO');
DECLARE @Mode uniqueidentifier =
    (SELECT TOP 1 Id FROM configuration.PaymentModes WHERE OrganizationId = @Org AND Name = N'Phase 90 Cash');
DECLARE @Cash uniqueidentifier =
    (SELECT TOP 1 Id FROM accounting.Accounts WHERE OrganizationId = @Org AND Kind = N'Cash' ORDER BY Code);

IF @WalkIn IS NULL OR @Warehouse IS NULL OR @Location IS NULL OR @Mode IS NULL OR @Cash IS NULL
BEGIN
    RAISERROR('Reference rows missing -- run seed-master.sh, seed-bulk.sql and seed-phase90.sql first.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sales.Invoices WHERE OrganizationId = @Org AND Channel = N'Pos')
BEGIN
    RAISERROR('This organization already has till sales -- seed-phase91.sql has run against it.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

-- The head office runs a Retail till from the tenant's warehouse; the tenant holds the entitlement.
UPDATE tenancy.TenantSubscriptions SET PosRetailEnabled = 1 WHERE OrganizationId = @Org;
UPDATE tenancy.BillingLocations SET PosMode = N'Retail', WarehouseId = @Warehouse WHERE Id = @Location;

-- Every tendered invoice is a till sale of the walk-in at the head office.
UPDATE inv SET ContactId = @WalkIn, Channel = N'Pos', LocationId = @Location
FROM sales.Invoices inv
WHERE inv.OrganizationId = @Org
  AND EXISTS (SELECT 1 FROM sales.InvoiceTenders t WHERE t.InvoiceId = inv.Id);
PRINT 'till sales=' + CAST(@@ROWCOUNT AS varchar(10));

-- Refunds: the first line of NumRefunds of those sales comes back, paid out in cash.
IF OBJECT_ID('tempdb..#Ref') IS NOT NULL DROP TABLE #Ref;
SELECT TOP (@NumRefunds)
       NoteId = NEWID(), LineId = NEWID(), inv.Id AS InvoiceId, inv.Code, inv.Date, i = ROW_NUMBER() OVER (ORDER BY inv.Code),
       l.ProductId, l.Quantity, l.Rate, l.VatRate, l.Amount, l.VatAmount, l.ConversionFactor, l.UnitId
INTO #Ref
FROM sales.Invoices inv
CROSS APPLY (SELECT TOP 1 * FROM sales.InvoiceLines x WHERE x.InvoiceId = inv.Id ORDER BY x.Id) l
WHERE inv.OrganizationId = @Org AND inv.Channel = N'Pos' AND inv.Status = N'Approved'
ORDER BY inv.Code;

INSERT INTO sales.CreditNotes
    (Id, OrganizationId, ContactId, Code, Date, Reference, Status, ApprovedByUserId, ApprovedAt, CreatedAt,
     ReferrerType, ReferrerId, CurrencyCode, ExchangeRate, LocationId, Channel, Reason, WarehouseId)
SELECT r.NoteId, @Org, @WalkIn, N'CN-P91-' + RIGHT(N'000000' + CAST(r.i AS nvarchar(10)), 6), r.Date, r.Code,
       N'Approved', NULL, CAST(r.Date AS datetimeoffset), CAST(r.Date AS datetimeoffset),
       N'Invoice', r.InvoiceId, N'NPR', 1, @Location, N'Pos', N'Phase 91 refund', @Warehouse
FROM #Ref r;
PRINT 'refunds=' + CAST(@@ROWCOUNT AS varchar(10));

INSERT INTO sales.CreditNoteLines
    (Id, CreditNoteId, ProductId, Quantity, Rate, VatRate, Amount, VatAmount, ConversionFactor, UnitId)
SELECT r.LineId, r.NoteId, r.ProductId, r.Quantity, r.Rate, r.VatRate, r.Amount, r.VatAmount, r.ConversionFactor, r.UnitId
FROM #Ref r;

INSERT INTO sales.CreditNotePayouts (Id, CreditNoteId, PaymentModeId, Kind, AccountId, Amount)
SELECT NEWID(), r.NoteId, @Mode, N'Cash', @Cash, r.Amount + r.VatAmount
FROM #Ref r;
PRINT 'payouts=' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT;
