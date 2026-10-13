-- Phase 90 -- the settlement rows the 34c seed has none of, so that OutstandingDocumentReader's
-- organization-wide settlement queries have something to read when a payment form asks for ONE
-- contact's open documents (ListOpenAllocationTargetsQuery, the FIFO Suggest).
--
-- On the 34c tenant PaymentAllocations, InvoiceTenders and JournalVouchers are all empty, so a
-- per-contact picker there looks cheap for the wrong reason: the queries that do not narrow by
-- contact are reading empty tables. This seeds, against the tenant's own approved invoices:
--   * NumReceipts approved Received payments, each allocated in full to one invoice (i % 5 in 1..3);
--   * NumTendered invoices paid by a till tender (i % 5 in 0, 4), against one Cash payment mode;
--   * NumVouchers approved journal vouchers, each with one customer-tagged debit line.
-- No GL is written: the reader under measurement reads documents, never the ledger.
--
-- Usage:
--   sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-phase90.sql \
--     -v OrgId="<guid>" NumReceipts=30000 NumTendered=20000 NumVouchers=10000

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

DECLARE @Org uniqueidentifier = '$(OrgId)';
DECLARE @NumReceipts int = $(NumReceipts);
DECLARE @NumTendered int = $(NumTendered);
DECLARE @NumVouchers int = $(NumVouchers);

DECLARE @Cash uniqueidentifier =
    (SELECT TOP 1 Id FROM accounting.Accounts WHERE OrganizationId = @Org AND Kind = N'Cash' ORDER BY Code);
DECLARE @Other uniqueidentifier =
    (SELECT TOP 1 Id FROM accounting.Accounts WHERE OrganizationId = @Org AND Kind = N'Other' ORDER BY Code);
DECLARE @Currency nvarchar(3) = (SELECT TOP 1 CurrencyCode FROM sales.Invoices WHERE OrganizationId = @Org);

IF @Cash IS NULL OR @Other IS NULL OR @Currency IS NULL
BEGIN
    RAISERROR('Reference rows missing -- run seed-master.sh and seed-bulk.sql against this organization first.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sales.InvoiceTenders t JOIN sales.Invoices i ON i.Id = t.InvoiceId WHERE i.OrganizationId = @Org)
BEGIN
    RAISERROR('This organization already has tenders -- seed-phase90.sql has run against it.', 16, 1);
    RETURN;
END

-- Every approved invoice with its total, in a stable order.
IF OBJECT_ID('tempdb..#Inv') IS NOT NULL DROP TABLE #Inv;
SELECT i = ROW_NUMBER() OVER (ORDER BY inv.Code), inv.Id, inv.ContactId, inv.Date,
       Total = (SELECT SUM(l.Amount + l.ServiceChargeAmount + l.PackagingFeeAmount + l.VatAmount)
                FROM sales.InvoiceLines l WHERE l.InvoiceId = inv.Id) + inv.RoundOff
INTO #Inv
FROM sales.Invoices inv
WHERE inv.OrganizationId = @Org AND inv.Status = N'Approved';
CREATE UNIQUE CLUSTERED INDEX IX_Inv ON #Inv (i);

DECLARE @NumInvoices int = (SELECT COUNT(*) FROM #Inv);
PRINT 'approved invoices=' + CAST(@NumInvoices AS varchar(10));

-- Receipts: one approved payment per invoice, allocated in full.
IF OBJECT_ID('tempdb..#Rec') IS NOT NULL DROP TABLE #Rec;
SELECT TOP (@NumReceipts) PaymentId = NEWID(), v.Id AS InvoiceId, v.ContactId, v.Date, v.Total, v.i
INTO #Rec
FROM #Inv v WHERE v.i % 5 IN (1, 2, 3) AND v.Total > 0
ORDER BY v.i;

INSERT INTO payments.Payments
    (Id, OrganizationId, ContactId, Direction, Code, Date, PaymentModeId, AccountId, Amount,
     Reference, Status, ApprovedByUserId, ApprovedAt, CreatedAt, VoidedAt, VoidedByUserId,
     CurrencyCode, ExchangeRate, LocationId, UnallocatedControlAccountId)
SELECT r.PaymentId, @Org, r.ContactId, N'Received',
       N'RCPT-P90-' + RIGHT(N'000000' + CAST(r.i AS nvarchar(10)), 6), r.Date, NULL, @Cash, r.Total,
       N'Phase 90 receipt', N'Approved', NULL, CAST(r.Date AS datetimeoffset), CAST(r.Date AS datetimeoffset),
       NULL, NULL, @Currency, 1, NULL, NULL
FROM #Rec r;
PRINT 'receipts=' + CAST(@@ROWCOUNT AS varchar(10));

INSERT INTO payments.PaymentAllocations (Id, SourceId, TargetDocumentType, TargetDocumentId, Amount, SourceType)
SELECT NEWID(), r.PaymentId, N'Invoice', r.InvoiceId, r.Total, N'Payment'
FROM #Rec r;
PRINT 'allocations=' + CAST(@@ROWCOUNT AS varchar(10));

-- Tenders: a till sale paying itself.
DECLARE @Mode uniqueidentifier = NEWID();
INSERT INTO configuration.PaymentModes (Id, OrganizationId, Name, IsActive, CreatedAt, RequiresChequeDetails, AccountId, Kind)
VALUES (@Mode, @Org, N'Phase 90 Cash', 1, SYSDATETIMEOFFSET(), 0, @Cash, N'Cash');

INSERT INTO sales.InvoiceTenders (Id, InvoiceId, PaymentModeId, Kind, AccountId, Amount)
SELECT TOP (@NumTendered) NEWID(), v.Id, @Mode, N'Cash', @Cash, v.Total
FROM #Inv v WHERE v.i % 5 IN (0, 4) AND v.Total > 0
ORDER BY v.i;
PRINT 'tenders=' + CAST(@@ROWCOUNT AS varchar(10));

-- Journal vouchers: one customer-tagged debit, one untagged credit.
IF OBJECT_ID('tempdb..#Jv') IS NOT NULL DROP TABLE #Jv;
SELECT TOP (@NumVouchers) Id = NEWID(), v.i, v.ContactId, v.Date
INTO #Jv
FROM #Inv v WHERE v.i % 7 = 0
ORDER BY v.i;

INSERT INTO accounting.JournalVouchers
    (Id, OrganizationId, Code, Date, Reference, Status, ApprovedByUserId, ApprovedAt, CreatedAt,
     VoidedAt, VoidedByUserId, CurrencyCode, ExchangeRate, LocationId)
SELECT j.Id, @Org, N'JV-P90-' + RIGHT(N'000000' + CAST(j.i AS nvarchar(10)), 6), j.Date, NULL, N'Approved',
       NULL, CAST(j.Date AS datetimeoffset), CAST(j.Date AS datetimeoffset), NULL, NULL, @Currency, 1, NULL
FROM #Jv j;
PRINT 'vouchers=' + CAST(@@ROWCOUNT AS varchar(10));

INSERT INTO accounting.JournalVoucherLines (Id, JournalVoucherId, AccountId, Debit, Credit, ContactId)
SELECT NEWID(), j.Id, @Other, 500, 0, j.ContactId FROM #Jv j
UNION ALL
SELECT NEWID(), j.Id, @Cash, 0, 500, NULL FROM #Jv j;
PRINT 'voucher lines=' + CAST(@@ROWCOUNT AS varchar(10));
