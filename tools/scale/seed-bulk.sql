-- Phase 34c -- scale dataset, part 2 of 2: the 120,000 rows Decision C names, by direct INSERT.
--
-- Runs against the organization seed-master.sh created, and derives every column value it can from
-- the ONE reference row of each kind that the real API/MediatR handlers wrote (the reference invoice
-- and purchase bill, their lines and their GL entries). Anything not derived is stated here with a
-- reason. Producing this through the API instead would take hours and would measure the seeder
-- (Decision C).
--
-- Usage:
--   sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-bulk.sql \
--     -v OrgId="<guid>" NumInvoices=50000 NumContacts=50000 NumProducts=20000 NumBills=20000
--
-- Idempotence: this script APPENDS. Re-running it doubles the dataset. Seed a fresh organization
-- instead (seed-master.sh always creates one).

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

DECLARE @Org uniqueidentifier = '$(OrgId)';
DECLARE @NumInvoices int = $(NumInvoices);
DECLARE @NumContacts int = $(NumContacts);
DECLARE @NumProducts int = $(NumProducts);
DECLARE @NumBills    int = $(NumBills);

-- The reference rows. Every one of these was written by the real handler, so the bulk rows below
-- inherit the shape rather than restating it.
DECLARE @RefInvoice uniqueidentifier = (SELECT TOP 1 Id FROM sales.Invoices WHERE OrganizationId = @Org ORDER BY CreatedAt);
DECLARE @RefBill    uniqueidentifier = (SELECT TOP 1 Id FROM purchasing.PurchaseBills WHERE OrganizationId = @Org ORDER BY CreatedAt);
DECLARE @Warehouse  uniqueidentifier = (SELECT WarehouseId FROM sales.Invoices WHERE Id = @RefInvoice);
DECLARE @Category   uniqueidentifier = (SELECT TOP 1 CategoryId FROM catalog.Products WHERE OrganizationId = @Org);
DECLARE @Unit       uniqueidentifier = (SELECT TOP 1 PrimaryUnitId FROM catalog.Products WHERE OrganizationId = @Org);
DECLARE @Group      uniqueidentifier = (SELECT TOP 1 GroupId FROM contacts.Contacts WHERE OrganizationId = @Org AND GroupId IS NOT NULL);
DECLARE @ApprovedBy uniqueidentifier = (SELECT ApprovedByUserId FROM sales.Invoices WHERE Id = @RefInvoice);

-- The three GL accounts the Invoice posting rule used, and the three the PurchaseBill rule used --
-- read back off the reference entries rather than named, so a change to either rule shows up here
-- as different accounts instead of silently seeding a stale chart.
DECLARE @AcctAr    uniqueidentifier, @AcctSales uniqueidentifier, @AcctVatOut uniqueidentifier;
DECLARE @AcctAp    uniqueidentifier, @AcctPurch uniqueidentifier, @AcctVatIn  uniqueidentifier;

SELECT @AcctAr    = MAX(CASE WHEN gl.Debit  > 0 THEN gl.AccountId END),
       @AcctSales = MAX(CASE WHEN gl.Credit = 1000 THEN gl.AccountId END),
       @AcctVatOut= MAX(CASE WHEN gl.Credit = 130  THEN gl.AccountId END)
FROM accounting.GlLines gl
JOIN accounting.GlJournalEntries e ON e.Id = gl.GlJournalEntryId
WHERE e.SourceDocumentId = @RefInvoice;

SELECT @AcctAp    = MAX(CASE WHEN gl.Credit > 0 THEN gl.AccountId END),
       @AcctPurch = MAX(CASE WHEN gl.Debit  = 600 THEN gl.AccountId END),
       @AcctVatIn = MAX(CASE WHEN gl.Debit  = 78  THEN gl.AccountId END)
FROM accounting.GlLines gl
JOIN accounting.GlJournalEntries e ON e.Id = gl.GlJournalEntryId
WHERE e.SourceDocumentId = @RefBill;

IF @RefInvoice IS NULL OR @RefBill IS NULL OR @AcctAr IS NULL OR @AcctAp IS NULL
BEGIN
    RAISERROR('Reference rows missing -- run tools/scale/seed-master.sh against this organization first.', 16, 1);
    RETURN;
END

-- The business-date window: the organization's accounting start date to today, so every
-- date-ranged list and report has a real range to filter and the fiscal-year reports have three
-- years to choose from.
DECLARE @Start date = (SELECT AccountingStartDate FROM tenancy.Organizations WHERE Id = @Org);
DECLARE @Days  int  = DATEDIFF(day, @Start, CAST(SYSUTCDATETIME() AS date));

PRINT 'org=' + CAST(@Org AS varchar(40)) + ' start=' + CONVERT(varchar(10), @Start, 23) + ' days=' + CAST(@Days AS varchar(10));

-- ---------------------------------------------------------------------------------------------
-- A numbers table. Materialised and indexed once rather than re-derived per statement.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#N') IS NOT NULL DROP TABLE #N;
CREATE TABLE #N (n int NOT NULL PRIMARY KEY);
DECLARE @Max int = (SELECT MAX(v) FROM (VALUES (@NumInvoices), (@NumContacts), (@NumProducts), (@NumBills)) AS x(v));

INSERT INTO #N (n)
SELECT TOP (@Max) ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
FROM sys.all_columns a CROSS JOIN sys.all_columns b;

PRINT 'numbers=' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------------------------
-- Products. 20,000, Service-typed: a Goods line consumes stock regardless of TrackInventory
-- (phase-30 gotcha), so a Goods bulk row would owe StockLedgerEntry/StockMovement rows that no
-- posting rule here wrote. Service keeps every seeded document's GL shape exactly the reference
-- shape. Names are word-like so the 34b `search` term has something realistic to LIKE against.
-- ---------------------------------------------------------------------------------------------
DECLARE @Nouns TABLE (i int PRIMARY KEY, w nvarchar(20));
INSERT INTO @Nouns VALUES
 (0,N'Consulting'),(1,N'Installation'),(2,N'Maintenance'),(3,N'Delivery'),(4,N'Training'),
 (5,N'Inspection'),(6,N'Calibration'),(7,N'Support'),(8,N'Licensing'),(9,N'Assembly');

DECLARE @Adjs TABLE (i int PRIMARY KEY, w nvarchar(20));
INSERT INTO @Adjs VALUES
 (0,N'Annual'),(1,N'Onsite'),(2,N'Remote'),(3,N'Priority'),(4,N'Standard'),
 (5,N'Extended'),(6,N'Basic'),(7,N'Premium'),(8,N'Rapid'),(9,N'Certified');

INSERT INTO catalog.Products
    (Id, OrganizationId, Type, Name, Code, CategoryId, PrimaryUnitId, HsCode, AvailableForSale,
     SellingPrice, PurchasePrice, VatRate, ValuationMethod, ReOrderLevel, TrackInventory, IsActive,
     CreatedAt, PurchaseAccountId, PurchaseReturnAccountId, SalesAccountId, SalesReturnAccountId,
     Barcode, CombinationKey, HasVariants, ParentProductId, Sku)
SELECT NEWID(), @Org, N'Service',
       a.w + N' ' + nn.w + N' ' + CAST(n.n AS nvarchar(10)),
       N'SVC-' + RIGHT(N'000000' + CAST(n.n AS nvarchar(10)), 6),
       @Category, @Unit, NULL, 1,
       500 + (n.n % 40) * 25, 300 + (n.n % 30) * 15,
       N'ThirteenPercentVat', N'Fifo', 0, 0, 1,
       DATEADD(day, n.n % NULLIF(@Days, 0), CAST(@Start AS datetimeoffset)),
       NULL, NULL, NULL, NULL, NULL, NULL, 0, NULL, NULL
FROM #N n
JOIN @Nouns nn ON nn.i = n.n % 10
JOIN @Adjs  a  ON a.i  = (n.n / 10) % 10
WHERE n.n <= @NumProducts;

PRINT 'products=' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------------------------
-- Contacts. 50,000, 70% Customer / 30% Supplier -- a trading tenant's own mix, and enough
-- suppliers (15,000) that the purchase-side reports are not reading one row.
-- ---------------------------------------------------------------------------------------------
DECLARE @First TABLE (i int PRIMARY KEY, w nvarchar(20));
INSERT INTO @First VALUES
 (0,N'Himalayan'),(1,N'Everest'),(2,N'Annapurna'),(3,N'Kathmandu'),(4,N'Pokhara'),
 (5,N'Lumbini'),(6,N'Gandaki'),(7,N'Bagmati'),(8,N'Janakpur'),(9,N'Chitwan');

DECLARE @Second TABLE (i int PRIMARY KEY, w nvarchar(20));
INSERT INTO @Second VALUES
 (0,N'Traders'),(1,N'Suppliers'),(2,N'Enterprises'),(3,N'Distributors'),(4,N'Hardware'),
 (5,N'Electronics'),(6,N'Textiles'),(7,N'Agro'),(8,N'Builders'),(9,N'Logistics');

INSERT INTO contacts.Contacts
    (Id, OrganizationId, Type, Name, Code, Address, Pan, Phone, Email, GroupId, IsActive,
     OpeningBalance, CreatedAt, AcceptsReverseTransactions, CreditLimit, CreditTermId)
SELECT NEWID(), @Org,
       CASE WHEN n.n % 10 < 7 THEN N'Customer' ELSE N'Supplier' END,
       f.w + N' ' + s.w + N' ' + CAST(n.n AS nvarchar(10)),
       CASE WHEN n.n % 10 < 7 THEN N'CUS-' ELSE N'SUP-' END + RIGHT(N'000000' + CAST(n.n AS nvarchar(10)), 6),
       N'Ward ' + CAST(n.n % 32 + 1 AS nvarchar(3)) + N', Kathmandu',
       N'6' + RIGHT(N'00000000' + CAST(n.n AS nvarchar(10)), 8),
       N'98' + RIGHT(N'00000000' + CAST(n.n AS nvarchar(10)), 8),
       N'contact' + CAST(n.n AS nvarchar(10)) + N'@example.com',
       @Group, 1, 0,
       DATEADD(day, n.n % NULLIF(@Days, 0), CAST(@Start AS datetimeoffset)),
       0, 0, NULL
FROM #N n
JOIN @First  f ON f.i = n.n % 10
JOIN @Second s ON s.i = (n.n / 10) % 10
WHERE n.n <= @NumContacts;

PRINT 'contacts=' + CAST(@@ROWCOUNT AS varchar(10));

-- Round-robin pools. Materialised with a dense index so the document inserts join on an int
-- rather than re-ranking 50,000 rows per statement.
IF OBJECT_ID('tempdb..#Cust') IS NOT NULL DROP TABLE #Cust;
IF OBJECT_ID('tempdb..#Supp') IS NOT NULL DROP TABLE #Supp;
IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;

SELECT i = ROW_NUMBER() OVER (ORDER BY Code) - 1, Id INTO #Cust FROM contacts.Contacts WHERE OrganizationId = @Org AND Type = N'Customer';
SELECT i = ROW_NUMBER() OVER (ORDER BY Code) - 1, Id INTO #Supp FROM contacts.Contacts WHERE OrganizationId = @Org AND Type = N'Supplier';
SELECT i = ROW_NUMBER() OVER (ORDER BY Code) - 1, Id, SellingPrice, PurchasePrice INTO #Prod FROM catalog.Products WHERE OrganizationId = @Org AND Type = N'Service';

CREATE UNIQUE CLUSTERED INDEX IX_Cust ON #Cust (i);
CREATE UNIQUE CLUSTERED INDEX IX_Supp ON #Supp (i);
CREATE UNIQUE CLUSTERED INDEX IX_Prod ON #Prod (i);

DECLARE @NCust int = (SELECT COUNT(*) FROM #Cust);
DECLARE @NSupp int = (SELECT COUNT(*) FROM #Supp);
DECLARE @NProd int = (SELECT COUNT(*) FROM #Prod);
PRINT 'pools: customers=' + CAST(@NCust AS varchar(10)) + ' suppliers=' + CAST(@NSupp AS varchar(10)) + ' products=' + CAST(@NProd AS varchar(10));

-- ---------------------------------------------------------------------------------------------
-- Invoices: 50,000 approved, one Service line each, spread evenly across the business-date window.
--
-- Two seeding decisions worth naming:
--  * Status is Approved for all of them. A Draft has no document number and posts no GL, so a
--    dataset of drafts would leave every report and every register empty -- the opposite of what
--    is being measured. The one Draft path that matters (the list's Status filter) is exercised by
--    the reference rows plus the measurement's own ?status=Draft call.
--  * PostedAt is derived from the business date, not from "now". In production a document approved
--    on its own date has PostedAt within hours of Date; stamping all 50,000 with SYSUTCDATETIME()
--    would put every GL report's whole dataset inside one day and make a date-ranged statement
--    measure nothing (the phase-19 PostedAt gotcha, used forwards).
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#Inv') IS NOT NULL DROP TABLE #Inv;

SELECT n.n,
       Id       = NEWID(),
       EntryId  = NEWID(),
       LineId   = NEWID(),
       ContactId = c.Id,
       ProductId = p.Id,
       Dt       = DATEADD(day, CAST((CAST(n.n AS bigint) * @Days) / @NumInvoices AS int), @Start),
       Qty      = CAST(1 + (n.n % 9) AS decimal(18,4)),
       Rate     = CAST(p.SellingPrice AS decimal(18,4))
INTO #Inv
FROM #N n
JOIN #Cust c ON c.i = n.n % @NCust
JOIN #Prod p ON p.i = n.n % @NProd
WHERE n.n <= @NumInvoices;

CREATE UNIQUE CLUSTERED INDEX IX_Inv ON #Inv (n);

INSERT INTO sales.Invoices
    (Id, OrganizationId, ContactId, WarehouseId, Code, [Date], Reference, [Status],
     ApprovedByUserId, ApprovedAt, CreatedAt, ReferrerType, ReferrerId, VoidedAt, VoidedByUserId,
     DiscountPct, ExportCountry, ExportDeclarationDate, ExportDeclarationNo, IsExport, Terms,
     CurrencyCode, ExchangeRate, DueDate, LocationId)
SELECT i.Id, @Org, i.ContactId, @Warehouse,
       RIGHT(N'000000' + CAST(i.n + 1 AS nvarchar(10)), 6),
       i.Dt,
       N'SO-' + CAST(i.n AS nvarchar(10)),
       N'Approved', @ApprovedBy,
       TODATETIMEOFFSET(CAST(i.Dt AS datetime2(7)), 0),
       TODATETIMEOFFSET(CAST(i.Dt AS datetime2(7)), 0),
       NULL, NULL, NULL, NULL,
       0, NULL, NULL, NULL, 0, NULL,
       N'NPR', 1, DATEADD(day, 30, i.Dt), NULL
FROM #Inv i;

PRINT 'invoices=' + CAST(@@ROWCOUNT AS varchar(10));

INSERT INTO sales.InvoiceLines (Id, InvoiceId, ProductId, Quantity, Rate, VatRate, Amount, VatAmount, CogsUnitCost, DiscountPct)
SELECT i.LineId, i.Id, i.ProductId, i.Qty, i.Rate, N'ThirteenPercentVat',
       i.Qty * i.Rate, ROUND(i.Qty * i.Rate * 0.13, 4), NULL, 0
FROM #Inv i;

PRINT 'invoice lines=' + CAST(@@ROWCOUNT AS varchar(10));

INSERT INTO accounting.GlJournalEntries (Id, OrganizationId, SourceDocumentType, SourceDocumentId, PostedAt)
SELECT i.EntryId, @Org, N'Invoice', i.Id, TODATETIMEOFFSET(CAST(i.Dt AS datetime2(7)), 0)
FROM #Inv i;

PRINT 'invoice GL entries=' + CAST(@@ROWCOUNT AS varchar(10));

INSERT INTO accounting.GlLines (Id, GlJournalEntryId, AccountId, Debit, Credit)
SELECT NEWID(), i.EntryId, @AcctAr, ROUND(i.Qty * i.Rate * 1.13, 4), 0 FROM #Inv i
UNION ALL SELECT NEWID(), i.EntryId, @AcctSales, 0, i.Qty * i.Rate FROM #Inv i
UNION ALL SELECT NEWID(), i.EntryId, @AcctVatOut, 0, ROUND(i.Qty * i.Rate * 0.13, 4) FROM #Inv i;

PRINT 'invoice GL lines=' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------------------------
-- Purchase bills: 20,000 approved, same construction on the supplier side. Their presence is what
-- gives the P&L an expense half, the Purchase Register rows, and supplier ageing a population.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#Bill') IS NOT NULL DROP TABLE #Bill;

SELECT n.n,
       Id       = NEWID(),
       EntryId  = NEWID(),
       LineId   = NEWID(),
       ContactId = s.Id,
       ProductId = p.Id,
       Dt       = DATEADD(day, CAST((CAST(n.n AS bigint) * @Days) / @NumBills AS int), @Start),
       Qty      = CAST(1 + (n.n % 7) AS decimal(18,4)),
       Rate     = CAST(p.PurchasePrice AS decimal(18,4))
INTO #Bill
FROM #N n
JOIN #Supp s ON s.i = n.n % @NSupp
JOIN #Prod p ON p.i = n.n % @NProd
WHERE n.n <= @NumBills;

CREATE UNIQUE CLUSTERED INDEX IX_Bill ON #Bill (n);

INSERT INTO purchasing.PurchaseBills
    (Id, OrganizationId, ContactId, WarehouseId, Code, [Date], Reference, SupplierInvoiceReference,
     IsImport, ImportCountry, ImportDate, ImportDocumentNo, TdsTypeId, TdsAmount, [Status],
     ApprovedByUserId, ApprovedAt, CreatedAt, ReferrerType, ReferrerId, VoidedAt, VoidedByUserId,
     DiscountPct, CurrencyCode, ExchangeRate, AdditionalCostRoundingAdjustment,
     CapitalisedAdditionalCost, IsProductWiseAdditionalCost, DueDate, LocationId)
SELECT b.Id, @Org, b.ContactId, @Warehouse,
       RIGHT(N'000000' + CAST(b.n + 1 AS nvarchar(10)), 6),
       b.Dt,
       N'PO-' + CAST(b.n AS nvarchar(10)),
       N'SUP-' + RIGHT(N'000000' + CAST(b.n AS nvarchar(10)), 6),
       0, NULL, NULL, NULL, NULL, 0, N'Approved', @ApprovedBy,
       TODATETIMEOFFSET(CAST(b.Dt AS datetime2(7)), 0),
       TODATETIMEOFFSET(CAST(b.Dt AS datetime2(7)), 0),
       NULL, NULL, NULL, NULL, 0, N'NPR', 1, 0, 0, 0,
       DATEADD(day, 30, b.Dt), NULL
FROM #Bill b;

PRINT 'purchase bills=' + CAST(@@ROWCOUNT AS varchar(10));

INSERT INTO purchasing.PurchaseBillLines (Id, PurchaseBillId, ProductId, Quantity, Rate, VatRate, Amount, VatAmount, ExpenditureClassification, DiscountPct)
SELECT b.LineId, b.Id, b.ProductId, b.Qty, b.Rate, N'ThirteenPercentVat',
       b.Qty * b.Rate, ROUND(b.Qty * b.Rate * 0.13, 4), N'Others', 0
FROM #Bill b;

PRINT 'purchase bill lines=' + CAST(@@ROWCOUNT AS varchar(10));

INSERT INTO accounting.GlJournalEntries (Id, OrganizationId, SourceDocumentType, SourceDocumentId, PostedAt)
SELECT b.EntryId, @Org, N'PurchaseBill', b.Id, TODATETIMEOFFSET(CAST(b.Dt AS datetime2(7)), 0)
FROM #Bill b;

INSERT INTO accounting.GlLines (Id, GlJournalEntryId, AccountId, Debit, Credit)
SELECT NEWID(), b.EntryId, @AcctPurch, b.Qty * b.Rate, 0 FROM #Bill b
UNION ALL SELECT NEWID(), b.EntryId, @AcctVatIn, ROUND(b.Qty * b.Rate * 0.13, 4), 0 FROM #Bill b
UNION ALL SELECT NEWID(), b.EntryId, @AcctAp, 0, ROUND(b.Qty * b.Rate * 1.13, 4) FROM #Bill b;

PRINT 'purchase GL lines=' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------------------------
-- The document-numbering counters, so a later API-driven create does not re-issue a used number.
-- ---------------------------------------------------------------------------------------------
UPDATE r SET r.NextNumber = @NumInvoices + 2
FROM configuration.DocumentNumberingRules r
WHERE r.OrganizationId = @Org AND r.DocumentType = N'Invoice';

UPDATE r SET r.NextNumber = @NumBills + 2
FROM configuration.DocumentNumberingRules r
WHERE r.OrganizationId = @Org AND r.DocumentType = N'PurchaseBill';

-- ---------------------------------------------------------------------------------------------
-- What was produced, and the balance check that says the GL is still a ledger.
-- ---------------------------------------------------------------------------------------------
SELECT N'Products'  AS [table], COUNT(*) AS rows FROM catalog.Products WHERE OrganizationId = @Org
UNION ALL SELECT N'Contacts', COUNT(*) FROM contacts.Contacts WHERE OrganizationId = @Org
UNION ALL SELECT N'Invoices', COUNT(*) FROM sales.Invoices WHERE OrganizationId = @Org
UNION ALL SELECT N'InvoiceLines', COUNT(*) FROM sales.InvoiceLines l JOIN sales.Invoices i ON i.Id = l.InvoiceId WHERE i.OrganizationId = @Org
UNION ALL SELECT N'PurchaseBills', COUNT(*) FROM purchasing.PurchaseBills WHERE OrganizationId = @Org
UNION ALL SELECT N'GlJournalEntries', COUNT(*) FROM accounting.GlJournalEntries WHERE OrganizationId = @Org
UNION ALL SELECT N'GlLines', COUNT(*) FROM accounting.GlLines gl JOIN accounting.GlJournalEntries e ON e.Id = gl.GlJournalEntryId WHERE e.OrganizationId = @Org;

SELECT TotalDebit = SUM(gl.Debit), TotalCredit = SUM(gl.Credit), OutOfBalance = SUM(gl.Debit) - SUM(gl.Credit)
FROM accounting.GlLines gl
JOIN accounting.GlJournalEntries e ON e.Id = gl.GlJournalEntryId
WHERE e.OrganizationId = @Org;
