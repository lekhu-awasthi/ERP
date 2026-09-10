-- Phase 34c. Run this immediately before every measurement pass, so the two sides of a before/after
-- differ by the change under test and not by how good the optimizer's cardinality estimates happen
-- to be. It matters more here than it would in production: the dataset arrives by bulk INSERT, and
-- a report that joins to a line table can run 2x apart on sampled versus full statistics -- which is
-- how a first pass at this measurement produced a "improvement" that no index could explain.
--
-- Usage: sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/refresh-stats.sql

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

UPDATE STATISTICS sales.Invoices WITH FULLSCAN;
UPDATE STATISTICS sales.InvoiceLines WITH FULLSCAN;
UPDATE STATISTICS sales.CreditNotes WITH FULLSCAN;
UPDATE STATISTICS purchasing.PurchaseBills WITH FULLSCAN;
UPDATE STATISTICS purchasing.PurchaseBillLines WITH FULLSCAN;
UPDATE STATISTICS purchasing.DebitNotes WITH FULLSCAN;
UPDATE STATISTICS contacts.Contacts WITH FULLSCAN;
UPDATE STATISTICS catalog.Products WITH FULLSCAN;
UPDATE STATISTICS accounting.GlLines WITH FULLSCAN;
UPDATE STATISTICS accounting.GlJournalEntries WITH FULLSCAN;
UPDATE STATISTICS accounting.Accounts WITH FULLSCAN;
UPDATE STATISTICS payments.Payments WITH FULLSCAN;
UPDATE STATISTICS payments.PaymentAllocations WITH FULLSCAN;

-- A cold procedure cache, so neither pass inherits a plan compiled against the other's indexes.
DBCC FREEPROCCACHE WITH NO_INFOMSGS;

PRINT 'statistics refreshed, plan cache cleared';
