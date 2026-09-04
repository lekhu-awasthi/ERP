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
        invoice.AddLine(product.Id, 2, 100, VatRate.NoVat, 0);
        invoice.SetTerms("Payment due within 30 days.");
        db.Invoices.Add(invoice);

        db.PrintingTemplates.Add(PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true));

        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, DocumentType.Invoice, invoice.Id);

        Assert.Equal("Moonbeam Trading", dto.OrganizationName);
        Assert.Equal("Standard", dto.PrintingTemplateName);
        Assert.Equal("Invoice", dto.Title);
        Assert.Equal("Bill To", dto.PartyHeading);
        Assert.Equal("C-001 — Acme Traders", dto.PartyLabel);
        Assert.Equal("2026-08-01", dto.DateText);

        var items = Assert.Single(dto.Sections);
        Assert.Equal("Items", items.Title);
        var row = Assert.Single(items.Rows);
        Assert.Equal("P-001 — Widget", row.Cells[0]);
        Assert.Equal("200.00", row.Cells[^1]);

        Assert.Equal("200.00", dto.Summary.Single(x => x.Label == "Grand Total").Value);
        Assert.Equal("Payment due within 30 days.", dto.Terms);
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
        invoice.AddLine(product.Id, 1, 50, VatRate.NoVat, 0);
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
        transfer.AddLine(product.Id, 7);
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

    private static async Task<PrintableDocumentDto> Print(
        IAppDbContext db, Guid organizationId, DocumentType documentType, Guid documentId) =>
        await new PrintDocumentQueryHandler(db).Handle(
            new PrintDocumentQuery(organizationId, documentType, documentId), CancellationToken.None);

    private static Organization NewOrganization(IAppDbContext db)
    {
        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", "Kathmandu, Nepal", new DateOnly(2026, 1, 1), true, "moonbeam",
            "info@moonbeam.test", "01-4000000", "PAN12345", "https://moonbeam.test", Guid.NewGuid());
        db.Organizations.Add(organization);
        return organization;
    }
}
