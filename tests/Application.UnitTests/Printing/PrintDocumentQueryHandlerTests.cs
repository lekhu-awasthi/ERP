using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Printing;

public class PrintDocumentQueryHandlerTests
{
    [Fact]
    public async Task Handle_builds_an_items_section_for_invoice()
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        var organizationId = organization.Id;

        var contact = Contact.Create(organizationId, ContactType.Customer, "Acme Traders", "C-001", "Pokhara", null, null, null, null, 0);
        var product = Product.Create(
            organizationId, ProductType.Goods, "Widget", "P-001", Guid.NewGuid(), Guid.NewGuid(), null, true, 100, 80, VatRate.NoVat, 0, true);
        db.Contacts.Add(contact);
        db.Products.Add(product);

        var invoice = Invoice.Create(organizationId, contact.Id, Guid.NewGuid(), new DateOnly(2026, 8, 1), "REF-1", null, null);
        invoice.AddLine(product.Id, 2, 100, VatRate.NoVat, 0, null, 1m);
        invoice.SetTerms("Payment due within 30 days.");
        db.Invoices.Add(invoice);

        db.PrintingTemplates.Add(PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true));

        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, DocumentType.Invoice, invoice.Id);

        Assert.Equal("Moonbeam Trading", dto.OrganizationName);
        Assert.Equal("Standard", dto.PrintingTemplateName);
        // Phase 67: Moonbeam is VAT-registered, so its invoice is a tax invoice.
        Assert.Equal("Tax Invoice", dto.Title);
        Assert.Equal("Bill To", dto.PartyHeading);
        Assert.Equal("C-001 — Acme Traders", dto.PartyLabel);
        // Phase 71: a tax document's first statutory date, in BS with AD beside it, with the toggle at AD. A draft
        // has not been issued, so it prints no issue date.
        Assert.Equal("Transaction Date", dto.DateLabel);
        Assert.Equal("2083-04-16 BS (2026-08-01 AD)", dto.DateText);
        Assert.Null(dto.IssueDateText);

        var items = Assert.Single(dto.Sections);
        Assert.Equal("Items", items.Title);
        var row = Assert.Single(items.Rows);
        Assert.Equal("P-001 — Widget", row.Cells[0]);
        Assert.Equal("200.00", row.Cells[^1]);

        Assert.Equal("200.00", dto.Summary.Single(x => x.Label == "Grand Total").Value);
        // Phase 39: Terms is rich text, sanitised by Invoice.SetTerms, so plain text comes back
        // wrapped in a paragraph. RichTextPdfRenderer folds the same tree the browser renders.
        Assert.Equal("<p>Payment due within 30 days.</p>", dto.Terms);
    }

    [Fact]
    public async Task Handle_carries_the_buyer_pan_on_an_invoice_and_a_credit_note_only_when_the_contact_has_one()
    {
        var db = TestAppDbContext.Create();
        var organizationId = NewOrganization(db).Id;

        var registered = Contact.Create(organizationId, ContactType.Customer, "Acme Traders", "C-001", null, " 609876543 ", null, null, null, 0);
        var retail = Contact.Create(organizationId, ContactType.Customer, "Walk-in Buyer", "C-002", null, null, null, null, null, 0);
        var product = Product.Create(
            organizationId, ProductType.Service, "Repair", "S-001", Guid.NewGuid(), Guid.NewGuid(), null, true, 100, 80, VatRate.NoVat, 0, true);
        db.Contacts.AddRange(registered, retail);
        db.Products.Add(product);

        var invoice = Invoice.Create(organizationId, registered.Id, Guid.NewGuid(), new DateOnly(2026, 8, 1), null, null, null);
        invoice.AddLine(product.Id, 1, 100, VatRate.NoVat, 0, null, 1m);
        var retailInvoice = Invoice.Create(organizationId, retail.Id, Guid.NewGuid(), new DateOnly(2026, 8, 1), null, null, null);
        retailInvoice.AddLine(product.Id, 1, 100, VatRate.NoVat, 0, null, 1m);
        var note = CreditNote.Create(organizationId, registered.Id, new DateOnly(2026, 8, 2), null, null, null);
        note.AddLine(product.Id, 1, 100, VatRate.NoVat, 0, null, 1m);
        var retailNote = CreditNote.Create(organizationId, retail.Id, new DateOnly(2026, 8, 2), null, null, null);
        retailNote.AddLine(product.Id, 1, 100, VatRate.NoVat, 0, null, 1m);
        var quotation = Quotation.Create(organizationId, registered.Id, new DateOnly(2026, 8, 1), null, null);
        quotation.AddLine(product.Id, 1, 100, VatRate.NoVat, 0, null, 1m);
        db.Invoices.AddRange(invoice, retailInvoice);
        db.CreditNotes.AddRange(note, retailNote);
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("609876543", (await Print(db, organizationId, DocumentType.Invoice, invoice.Id)).PartyPan);
        Assert.Equal("609876543", (await Print(db, organizationId, DocumentType.CreditNote, note.Id)).PartyPan);
        Assert.Null((await Print(db, organizationId, DocumentType.Invoice, retailInvoice.Id)).PartyPan);
        Assert.Null((await Print(db, organizationId, DocumentType.CreditNote, retailNote.Id)).PartyPan);
        // The statutory rule is the tax invoice's; every other document's party block is unchanged.
        Assert.Null((await Print(db, organizationId, DocumentType.Quotation, quotation.Id)).PartyPan);
    }

    [Fact]
    public async Task Handle_falls_back_to_a_default_template_name_when_none_is_configured()
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        var organizationId = organization.Id;

        var contact = Contact.Create(organizationId, ContactType.Customer, "Acme Traders", "C-001", null, null, null, null, null, 0);
        var product = Product.Create(
            organizationId, ProductType.Goods, "Widget", "P-001", Guid.NewGuid(), Guid.NewGuid(), null, true, 100, 80, VatRate.NoVat, 0, true);
        db.Contacts.Add(contact);
        db.Products.Add(product);

        var invoice = Invoice.Create(organizationId, contact.Id, Guid.NewGuid(), new DateOnly(2026, 8, 1), null, null, null);
        invoice.AddLine(product.Id, 1, 50, VatRate.NoVat, 0, null, 1m);
        db.Invoices.Add(invoice);

        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, DocumentType.Invoice, invoice.Id);

        Assert.Equal("Default", dto.PrintingTemplateName);
        Assert.Null(dto.Terms);
    }

    [Fact]
    public async Task Handle_builds_a_debit_credit_section_for_journal_voucher()
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        var organizationId = organization.Id;

        var groupId = Guid.NewGuid();
        var cashAccount = Account.Create(organizationId, "1000", "Cash", AccountRootType.Asset, groupId);
        var salaryAccount = Account.Create(organizationId, "5000", "Salary Expense", AccountRootType.Expense, groupId);
        db.Accounts.AddRange(cashAccount, salaryAccount);

        var voucher = JournalVoucher.Create(organizationId, new DateOnly(2026, 8, 1), "Aug salary");
        voucher.AddLine(salaryAccount.Id, 5000, 0);
        voucher.AddLine(cashAccount.Id, 0, 5000);
        db.JournalVouchers.Add(voucher);

        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, DocumentType.JournalVoucher, voucher.Id);

        Assert.Null(dto.PartyLabel);
        var entries = Assert.Single(dto.Sections);
        Assert.Equal("Entries", entries.Title);
        Assert.Equal(2, entries.Rows.Count);
        Assert.Contains(entries.Rows, r => r.Cells[0] == "5000 — Salary Expense" && r.Cells[1] == "5,000.00");
        Assert.Contains(entries.Rows, r => r.Cells[0] == "1000 — Cash" && r.Cells[2] == "5,000.00");
        Assert.Equal("5,000.00", entries.TotalRow!.Cells[1]);
    }

    /// <summary>Phase 27b's structural point: a Cash Transfer prints <b>two</b> tables, which
    /// phase-20d's single-collection DTO could not express.</summary>
    [Fact]
    public async Task Handle_builds_two_sections_for_cash_transfer()
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        var organizationId = organization.Id;

        var groupId = Guid.NewGuid();
        var from = Account.Create(organizationId, "1000", "Cash", AccountRootType.Asset, groupId);
        var to = Account.Create(organizationId, "1010", "Bank", AccountRootType.Asset, groupId);
        db.Accounts.AddRange(from, to);

        var transfer = CashTransfer.Create(organizationId, new DateOnly(2026, 8, 1), "Float", from.Id);
        transfer.AddLine(to.Id, 2500);
        db.CashTransfers.Add(transfer);

        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, DocumentType.CashTransfer, transfer.Id);

        Assert.Equal("Transfer", dto.Title);
        Assert.Equal(2, dto.Sections.Count);
        Assert.Equal("Transferred From", dto.Sections[0].Title);
        Assert.Equal("1000 — Cash", dto.Sections[0].Rows.Single().Cells[0]);
        Assert.Equal("Transferred To", dto.Sections[1].Title);
        Assert.Equal("1010 — Bank", dto.Sections[1].Rows.Single().Cells[0]);
        Assert.Equal("2,500.00", dto.Sections[1].TotalRow!.Cells[1]);
    }

    /// <summary>A Warehouse Transfer carries no money at all, and prints anyway -- the live pass's
    /// finding that print is universal, not reserved for financial documents.</summary>
    [Fact]
    public async Task Handle_builds_a_quantity_only_section_for_warehouse_transfer()
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        var organizationId = organization.Id;

        var fromWarehouse = Warehouse.Create(organizationId, "Kathmandu");
        var toWarehouse = Warehouse.Create(organizationId, "Patan");
        db.Warehouses.AddRange(fromWarehouse, toWarehouse);

        var product = Product.Create(
            organizationId, ProductType.Goods, "Widget", "P-001", Guid.NewGuid(), Guid.NewGuid(), null, true, 100, 80, VatRate.NoVat, 0, true);
        db.Products.Add(product);

        var transfer = WarehouseTransfer.Create(organizationId, fromWarehouse.Id, toWarehouse.Id, new DateOnly(2026, 8, 1), null);
        transfer.AddLine(product.Id, 7, null, 1m);
        db.WarehouseTransfers.Add(transfer);

        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, DocumentType.WarehouseTransfer, transfer.Id);

        Assert.Equal("Warehouse Transfer", dto.Title);
        Assert.Contains(dto.HeaderFields, f => f.Label == "From Warehouse" && f.Value == "Kathmandu");
        Assert.Contains(dto.HeaderFields, f => f.Label == "To Warehouse" && f.Value == "Patan");

        var items = Assert.Single(dto.Sections);
        Assert.Equal(["P-001 — Widget", "7"], items.Rows.Single().Cells);
    }

    /// <summary>An unallocated receipt still prints its "Payment For" table, empty. See the
    /// handler's own note on why the section is not dropped.</summary>
    [Fact]
    public async Task Handle_prints_an_empty_allocation_section_for_an_unallocated_payment()
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        var organizationId = organization.Id;

        var contact = Contact.Create(organizationId, ContactType.Customer, "Acme Traders", "C-001", null, null, null, null, null, 0);
        var account = Account.Create(organizationId, "1000", "Cash", AccountRootType.Asset, Guid.NewGuid());
        db.Contacts.Add(contact);
        db.Accounts.Add(account);

        var payment = Payment.Create(
            organizationId, contact.Id, PaymentDirection.Received, new DateOnly(2026, 8, 1), null, account.Id, 1200, null);
        db.Payments.Add(payment);

        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, DocumentType.Payment, payment.Id);

        Assert.Equal("Customer Receipt", dto.Title);
        Assert.Equal("Received From", dto.PartyHeading);
        Assert.Equal(2, dto.Sections.Count);
        Assert.Equal("Payment For", dto.Sections[1].Title);
        Assert.Empty(dto.Sections[1].Rows);
        Assert.Null(dto.Sections[1].TotalRow);
        Assert.Equal("1,200.00", dto.Summary.Single(x => x.Label == "Unallocated").Value);
    }

    /// <summary>Phase 84 -- the money receipt is a bill a customer is handed, so its date prints in BS with AD beside
    /// it whatever the toggle says. It is not a tax document: one "Date", no issue date, and nothing locked.</summary>
    [Theory]
    [InlineData(PaymentDirection.Received, CalendarFormat.Ad)]
    [InlineData(PaymentDirection.Received, CalendarFormat.Bs)]
    [InlineData(PaymentDirection.Paid, CalendarFormat.Ad)]
    public async Task Handle_dates_a_money_receipt_in_bs_with_ad_beside_it_whatever_the_toggle(
        PaymentDirection direction, CalendarFormat requested)
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        var organizationId = organization.Id;

        var contact = Contact.Create(organizationId, ContactType.Customer, "Acme Traders", "C-001", null, null, null, null, null, 0);
        var account = Account.Create(organizationId, "1000", "Cash", AccountRootType.Asset, Guid.NewGuid());
        db.Contacts.Add(contact);
        db.Accounts.Add(account);
        var payment = Payment.Create(organizationId, contact.Id, direction, new DateOnly(2026, 8, 1), null, account.Id, 1200, null);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            RequestCalendar.Current = requested;
            var dto = await Print(db, organizationId, DocumentType.Payment, payment.Id);

            Assert.Equal("2083-04-16 BS (2026-08-01 AD)", dto.DateText);
            Assert.Equal("Date", dto.DateLabel);
            Assert.Null(dto.IssueDateText);
            Assert.Null(dto.CalendarNote);
        }
        finally
        {
            RequestCalendar.Current = CalendarFormat.Ad;
        }
    }

    /// <summary>Phase 27b's other half: with the request's calendar set to BS, the same document's
    /// dates come out in Bikram Sambat -- closing phase-23 Decision A's "server output stays AD".
    /// 2026-08-01 AD is 2083-04-16 BS.</summary>
    [Fact]
    public async Task Handle_renders_dates_in_bikram_sambat_when_the_request_asked_for_it()
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        var organizationId = organization.Id;

        var voucher = JournalVoucher.Create(organizationId, new DateOnly(2026, 8, 1), null);
        var account = Account.Create(organizationId, "1000", "Cash", AccountRootType.Asset, Guid.NewGuid());
        db.Accounts.Add(account);
        voucher.AddLine(account.Id, 100, 0);
        voucher.AddLine(account.Id, 0, 100);
        db.JournalVouchers.Add(voucher);

        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            RequestCalendar.Current = CalendarFormat.Bs;
            var dto = await Print(db, organizationId, DocumentType.JournalVoucher, voucher.Id);

            Assert.Equal("2083-04-16", dto.DateText);
            Assert.Equal("Dates shown in Bikram Sambat (BS)", dto.CalendarNote);
        }
        finally
        {
            RequestCalendar.Current = CalendarFormat.Ad;
        }
    }

    /// <summary>Phase 71 -- a tax document prints every date in BS with AD beside it, whichever calendar the request
    /// asked for, and both statutory dates: the transaction date it bears and the Nepal day it was issued
    /// (approved). The bills here are backdated to 2026-08-01, so the two differ.</summary>
    [Theory]
    [InlineData(DocumentType.Invoice, CalendarFormat.Ad)]
    [InlineData(DocumentType.Invoice, CalendarFormat.Bs)]
    [InlineData(DocumentType.CreditNote, CalendarFormat.Ad)]
    [InlineData(DocumentType.CreditNote, CalendarFormat.Bs)]
    [InlineData(DocumentType.DebitNote, CalendarFormat.Ad)]
    [InlineData(DocumentType.DebitNote, CalendarFormat.Bs)]
    public async Task Handle_prints_a_tax_documents_dates_in_bs_with_ad_beside_them_whatever_the_toggle(
        DocumentType type, CalendarFormat requested)
    {
        var db = TestAppDbContext.Create();
        var organizationId = NewOrganization(db).Id;

        var contact = Contact.Create(organizationId, ContactType.Customer, "Acme Traders", "C-001", null, null, null, null, null, 0);
        var product = Product.Create(
            organizationId, ProductType.Service, "Repair", "S-001", Guid.NewGuid(), Guid.NewGuid(), null, true, 100, 80, VatRate.NoVat, 0, true);
        db.Contacts.Add(contact);
        db.Products.Add(product);

        var date = new DateOnly(2026, 8, 1);
        Guid id;
        DateTimeOffset approvedAt;
        switch (type)
        {
            case DocumentType.Invoice:
                var invoice = Invoice.Create(organizationId, contact.Id, Guid.NewGuid(), date, null, null, null);
                invoice.AddLine(product.Id, 1, 100, VatRate.NoVat, 0, null, 1m);
                invoice.Approve(Guid.NewGuid(), "INV0001");
                db.Invoices.Add(invoice);
                (id, approvedAt) = (invoice.Id, invoice.ApprovedAt!.Value);
                break;
            case DocumentType.CreditNote:
                var note = CreditNote.Create(organizationId, contact.Id, date, null, null, null);
                note.AddLine(product.Id, 1, 100, VatRate.NoVat, 0, null, 1m);
                note.Approve(Guid.NewGuid(), "CN0001");
                db.CreditNotes.Add(note);
                (id, approvedAt) = (note.Id, note.ApprovedAt!.Value);
                break;
            default:
                var debit = Domain.Purchasing.DebitNote.Create(organizationId, contact.Id, date, null, null, 0, null, null);
                debit.AddLine(product.Id, 1, 100, VatRate.NoVat, 0, null, 1m);
                debit.Approve(Guid.NewGuid(), "DN0001");
                db.DebitNotes.Add(debit);
                (id, approvedAt) = (debit.Id, debit.ApprovedAt!.Value);
                break;
        }

        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            RequestCalendar.Current = requested;
            var dto = await Print(db, organizationId, type, id);

            Assert.Equal("Transaction Date", dto.DateLabel);
            Assert.Equal("2083-04-16 BS (2026-08-01 AD)", dto.DateText);
            Assert.Equal(BillDate.Format(NepalTime.LocalDate(approvedAt)), dto.IssueDateText);
            Assert.NotEqual(dto.DateText, dto.IssueDateText);
            Assert.Null(dto.CalendarNote);

            if (dto.PrintedCopy is { } copy)
            {
                Assert.StartsWith(BillDate.Format(NepalTime.LocalDate(DateTimeOffset.UtcNow)), copy.PrintedAtText);
            }
        }
        finally
        {
            RequestCalendar.Current = CalendarFormat.Ad;
        }
    }

    /// <summary>Phase 71 -- an abbreviated tax invoice's form has one date (Schedule 6), so its PDF keeps the one "Date",
    /// still in BS with AD beside it, as its till receipt does. The flag is set through the change tracker: only a
    /// settled till sale can be issued abbreviated, and that state is not this test's subject.</summary>
    [Fact]
    public async Task Handle_prints_an_abbreviated_tax_invoice_with_its_one_date()
    {
        var db = TestAppDbContext.Create();
        var organizationId = NewOrganization(db).Id;
        var contact = Contact.Create(organizationId, ContactType.Customer, "Walk-in", "C-001", null, null, null, null, null, 0);
        var product = Product.Create(
            organizationId, ProductType.Service, "Tea", "S-001", Guid.NewGuid(), Guid.NewGuid(), null, true, 100, 80, VatRate.NoVat, 0, true);
        db.Contacts.Add(contact);
        db.Products.Add(product);

        var invoice = Invoice.Create(organizationId, contact.Id, Guid.NewGuid(), new DateOnly(2026, 8, 1), null, null, null);
        invoice.AddLine(product.Id, 1, 100, VatRate.NoVat, 0, null, 1m);
        invoice.Approve(Guid.NewGuid(), "INV0001");
        db.Invoices.Add(invoice);
        ((Microsoft.EntityFrameworkCore.DbContext)db).Entry(invoice).Property(nameof(Invoice.IsAbbreviatedTaxInvoice)).CurrentValue = true;
        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, DocumentType.Invoice, invoice.Id);

        Assert.Equal("Abbreviated Tax Invoice", dto.Title);
        Assert.Equal("Date", dto.DateLabel);
        Assert.Equal("2083-04-16 BS (2026-08-01 AD)", dto.DateText);
        Assert.Null(dto.IssueDateText);
    }

    /// <summary>Phase 71's boundary: a type that is not a tax document still follows the request's calendar and
    /// keeps its one "Date".</summary>
    [Fact]
    public async Task Handle_leaves_every_other_type_on_the_requests_calendar()
    {
        var db = TestAppDbContext.Create();
        var organizationId = NewOrganization(db).Id;

        var voucher = JournalVoucher.Create(organizationId, new DateOnly(2026, 8, 1), null);
        var account = Account.Create(organizationId, "1000", "Cash", AccountRootType.Asset, Guid.NewGuid());
        db.Accounts.Add(account);
        voucher.AddLine(account.Id, 100, 0);
        voucher.AddLine(account.Id, 0, 100);
        voucher.Approve(Guid.NewGuid(), "JV0001");
        db.JournalVouchers.Add(voucher);
        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, DocumentType.JournalVoucher, voucher.Id);

        Assert.Equal("Date", dto.DateLabel);
        Assert.Equal("2026-08-01", dto.DateText);
        Assert.Null(dto.IssueDateText);
    }

    [Fact]
    public void BillDate_labels_both_halves_and_falls_back_to_a_labelled_ad_date_outside_the_table()
    {
        Assert.Equal("2083-04-16 BS (2026-08-01 AD)", BillDate.Format(new DateOnly(2026, 8, 1)));
        Assert.Equal("2083-04-16", BillDate.Bs(new DateOnly(2026, 8, 1)));
        Assert.Equal("2040-01-01 AD", BillDate.Format(new DateOnly(2040, 1, 1)));
        Assert.Null(BillDate.Bs(new DateOnly(2040, 1, 1)));
    }

    private static async Task<PrintableDocumentDto> Print(
        IAppDbContext db, Guid organizationId, DocumentType documentType, Guid documentId) =>
        await new PrintDocumentQueryHandler(db, new FakeFileStorage()).Handle(
            new PrintDocumentQuery(
                organizationId,
                documentType,
                documentId,
                // Phase 67: an invoice or credit note renders only as a counted print.
                CountedPrints.Applies(documentType) ? new DocumentPrintIssue(1, Guid.NewGuid(), DateTimeOffset.UtcNow) : null),
            CancellationToken.None);

    private static Organization NewOrganization(IAppDbContext db)
    {
        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", "Kathmandu, Nepal", new DateOnly(2026, 1, 1), true, "moonbeam",
            "info@moonbeam.test", "01-4000000", "PAN12345", "https://moonbeam.test", Guid.NewGuid());
        db.Organizations.Add(organization);
        return organization;
    }
}
