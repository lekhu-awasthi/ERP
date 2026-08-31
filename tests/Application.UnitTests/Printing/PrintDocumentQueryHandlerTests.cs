using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Printing;

public class PrintDocumentQueryHandlerTests
{
    [Fact]
    public async Task Handle_builds_a_line_item_document_for_invoice()
    {
        var db = TestAppDbContext.Create();

        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", "Kathmandu, Nepal", new DateOnly(2026, 1, 1), true, "moonbeam",
            "info@moonbeam.test", "01-4000000", "PAN12345", "https://moonbeam.test", Guid.NewGuid());
        db.Organizations.Add(organization);
        var organizationId = organization.Id;

        var contact = Contact.Create(organizationId, ContactType.Customer, "Acme Traders", "C-001", "Pokhara", null, null, null, null, 0);
        var product = Product.Create(
            organizationId, ProductType.Goods, "Widget", "P-001", Guid.NewGuid(), Guid.NewGuid(), null, true, 100, 80, VatRate.NoVat, 0, true);
        db.Contacts.Add(contact);
        db.Products.Add(product);

        var invoice = Invoice.Create(organizationId, contact.Id, Guid.NewGuid(), new DateOnly(2026, 8, 1), "REF-1", null, null);
        invoice.AddLine(product.Id, 2, 100, VatRate.NoVat, 0);
        db.Invoices.Add(invoice);

        db.PrintingTemplates.Add(PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true));

        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new PrintDocumentQueryHandler(db);
        var dto = await handler.Handle(new PrintDocumentQuery(organizationId, DocumentType.Invoice, invoice.Id), CancellationToken.None);

        Assert.Equal("Moonbeam Trading", dto.OrganizationName);
        Assert.Equal("Standard", dto.PrintingTemplateName);
        Assert.Equal("C-001 — Acme Traders", dto.PartyLabel);
        Assert.NotNull(dto.Lines);
        Assert.Single(dto.Lines!);
        Assert.Equal("P-001 — Widget", dto.Lines![0].ProductLabel);
        Assert.Equal(200, dto.GrandTotal);
        Assert.Null(dto.GlLines);
    }

    [Fact]
    public async Task Handle_falls_back_to_a_default_template_name_when_none_is_configured()
    {
        var db = TestAppDbContext.Create();

        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", null, new DateOnly(2026, 1, 1), true, "moonbeam",
            null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
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

        var handler = new PrintDocumentQueryHandler(db);
        var dto = await handler.Handle(new PrintDocumentQuery(organizationId, DocumentType.Invoice, invoice.Id), CancellationToken.None);

        Assert.Equal("Default", dto.PrintingTemplateName);
    }

    [Fact]
    public async Task Handle_builds_a_ledger_document_for_journal_voucher()
    {
        var db = TestAppDbContext.Create();

        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", null, new DateOnly(2026, 1, 1), true, "moonbeam",
            null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
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

        var handler = new PrintDocumentQueryHandler(db);
        var dto = await handler.Handle(new PrintDocumentQuery(organizationId, DocumentType.JournalVoucher, voucher.Id), CancellationToken.None);

        Assert.Null(dto.Lines);
        Assert.NotNull(dto.GlLines);
        Assert.Equal(2, dto.GlLines!.Count);
        Assert.Contains(dto.GlLines!, l => l.AccountLabel == "5000 — Salary Expense" && l.Debit == 5000);
        Assert.Contains(dto.GlLines!, l => l.AccountLabel == "1000 — Cash" && l.Credit == 5000);
        Assert.Equal(5000, dto.GrandTotal);
        Assert.Equal("Default", dto.PrintingTemplateName);
    }
}
