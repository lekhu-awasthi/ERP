-- Phase 50 -- scale dataset, the Cheque half. seed-bulk.sql predates the Cheque Register being a
-- measured screen, so the table it reads held FIVE rows in the whole database and a pass against it
-- would have been the empty-tenant reading the harness's own README warns about.
--
-- Usage (after seed-bulk.sql, against the same organization):
--   sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-cheques.sql \
--     -v OrgId="<guid>" NumCheques=50000
--
-- Idempotence: this script APPENDS, exactly like seed-bulk.sql. Re-running it doubles the cheques.
--
-- ---------------------------------------------------------------------------------------------
-- Two seeding decisions, both of which change what the numbers mean, so both are stated here.
--
-- 1. The payments are DRAFT. A Cheque needs a Payment to hang off (ListChequesQuery joins through
--    it to the Contact), so 50,000 cheques owe 50,000 payments. seed-bulk.sql's governing rule is
--    that a seeded row never owes rows no posting rule wrote -- it is why its products are
--    Service-typed -- and an APPROVED payment owes a GL entry and an allocation. A Draft payment
--    owes neither: it is a consistent state of this aggregate, it carries Code = 'DRAFT' because a
--    document number is assigned at Approve, and (OrganizationId, Code) on Payments is not unique,
--    so fifty thousand of them is legal. It also leaves GlLines untouched, which is what keeps the
--    three financial statements comparable across this phase's own before/after pair.
--
-- 2. The cheques carry the full status mix anyway, though their payments are Draft. Phase 17 pairs
--    a Pending cheque with a Draft payment, so this is a divergence and not an oversight. It costs
--    nothing in the paths under measurement and buys one that matters: NEITHER query that reads
--    this table reads payment.Status -- ListChequesQueryHandler joins Payments only for ContactId,
--    and ChequeDashboardSummaryQueryHandler touches it only for the optional ContactId filter -- so
--    the mix is invisible to both, while a table of nothing but Pending would make the register's
--    two status tabs and its dashboard counts a measurement of one value.
-- ---------------------------------------------------------------------------------------------

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

DECLARE @Org uniqueidentifier = '$(OrgId)';
DECLARE @NumCheques int = $(NumCheques);

-- Derived, never named: the bank-ish account is the one the chart marks Kind = 'Cash', and the
-- window is the organization's own accounting start date to today -- the same window seed-bulk.sql
-- spread its documents over, so a date range that selects a third of the invoices selects about a
-- third of the cheques too.
DECLARE @Account uniqueidentifier =
    (SELECT TOP 1 Id FROM accounting.Accounts
      WHERE OrganizationId = @Org AND Kind = N'Cash' ORDER BY Code);

DECLARE @Currency nvarchar(3) = (SELECT TOP 1 CurrencyCode FROM sales.Invoices WHERE OrganizationId = @Org);
DECLARE @Start date = (SELECT AccountingStartDate FROM tenancy.Organizations WHERE Id = @Org);
DECLARE @Days  int  = DATEDIFF(day, @Start, CAST(SYSUTCDATETIME() AS date));

IF @Account IS NULL OR @Currency IS NULL OR @Days IS NULL OR @Days <= 0
BEGIN
    RAISERROR('Reference rows missing -- run tools/scale/seed-master.sh and seed-bulk.sql against this organization first.', 16, 1);
    RETURN;
END

PRINT 'org=' + CAST(@Org AS varchar(40)) + ' account=' + CAST(@Account AS varchar(40))
    + ' start=' + CONVERT(varchar(10), @Start, 23) + ' days=' + CAST(@Days AS varchar(10));

IF OBJECT_ID('tempdb..#CN') IS NOT NULL DROP TABLE #CN;
CREATE TABLE #CN (n int NOT NULL PRIMARY KEY);
INSERT INTO #CN (n)
SELECT TOP (@NumCheques) ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
FROM sys.all_columns a CROSS JOIN sys.all_columns b;

-- The contact pool, ranked exactly as seed-bulk.sql ranks its own, so a cheque's contact is a real
-- one and the join back to Contacts.Name is a join to fifty thousand distinct names.
IF OBJECT_ID('tempdb..#PC') IS NOT NULL DROP TABLE #PC;
SELECT i = ROW_NUMBER() OVER (ORDER BY Code) - 1, Id
INTO #PC FROM contacts.Contacts WHERE OrganizationId = @Org;
CREATE UNIQUE CLUSTERED INDEX IX_PC ON #PC (i);
DECLARE @NPC int = (SELECT COUNT(*) FROM #PC);

IF @NPC = 0
BEGIN
    RAISERROR('No contacts on this organization -- run tools/scale/seed-bulk.sql first.', 16, 1);
    RETURN;
END

-- ---------------------------------------------------------------------------------------------
-- The payments. Draft, so nothing downstream is owed. Direction alternates because the register's
-- two tabs are Received and Issued and each one is a measured path.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#Pay') IS NOT NULL DROP TABLE #Pay;
CREATE TABLE #Pay (n int NOT NULL PRIMARY KEY, Id uniqueidentifier NOT NULL, Dir nvarchar(20) NOT NULL, D date NOT NULL);

INSERT INTO #Pay (n, Id, Dir, D)
SELECT n.n, NEWID(),
       CASE WHEN n.n % 2 = 0 THEN N'Received' ELSE N'Paid' END,
       DATEADD(day, n.n % @Days, @Start)
FROM #CN n;

INSERT INTO payments.Payments
    (Id, OrganizationId, ContactId, Direction, Code, Date, PaymentModeId, AccountId, Amount,
     Reference, Status, ApprovedByUserId, ApprovedAt, CreatedAt, VoidedAt, VoidedByUserId,
     CurrencyCode, ExchangeRate, LocationId)
SELECT p.Id, @Org, c.Id, p.Dir, N'DRAFT', p.D, NULL, @Account,
       1000 + (p.n % 90) * 250,
       N'Cheque settlement ' + CAST(p.n AS nvarchar(10)),
       N'Draft', NULL, NULL,
       CAST(p.D AS datetimeoffset), NULL, NULL,
       @Currency, 1, NULL
FROM #Pay p
JOIN #PC c ON c.i = p.n % @NPC;

PRINT 'payments=' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------------------------
-- The cheques. ChequeDate is deliberately NOT the payment's Date -- a cheque is post-dated by
-- nought to twenty days here -- so a date-range filter over ChequeDate cannot accidentally be
-- satisfied by the Payments index the join already has.
--
-- ChequeNo is 'CHQ-' + a zero-padded ordinal: a term like 'CHQ-0004' hits a bounded slice and
-- 'ZZQQXX' misses everything, which is the pair phase 34c's search rows are built from.
-- ---------------------------------------------------------------------------------------------
INSERT INTO payments.Cheques
    (Id, OrganizationId, LinkedPaymentId, Direction, AccountId, ChequeNo, ChequeDate,
     ReceivedDate, Amount, Status, CreatedAt)
SELECT NEWID(), @Org, p.Id, p.Dir, @Account,
       N'CHQ-' + RIGHT(N'000000' + CAST(p.n AS nvarchar(10)), 6),
       DATEADD(day, p.n % 21, p.D),
       CASE WHEN p.Dir = N'Received' THEN p.D ELSE NULL END,
       1000 + (p.n % 90) * 250,
       -- 40 / 25 / 25 / 10. Pending dominates because an open cheque is what a register is for.
       CASE
           WHEN p.n % 20 < 8  THEN N'Pending'
           WHEN p.n % 20 < 13 THEN N'Deposited'
           WHEN p.n % 20 < 18 THEN N'Cleared'
           ELSE N'Bounced'
       END,
       CAST(p.D AS datetimeoffset)
FROM #Pay p;

PRINT 'cheques=' + CAST(@@ROWCOUNT AS varchar(10));

SELECT Status, Direction, COUNT(*) AS rows_seeded
FROM payments.Cheques WHERE OrganizationId = @Org
GROUP BY Status, Direction ORDER BY Status, Direction;
