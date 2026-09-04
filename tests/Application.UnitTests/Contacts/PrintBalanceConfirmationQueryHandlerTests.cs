using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.Contacts.Queries.PrintBalanceConfirmation;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Contacts;

public class PrintBalanceConfirmationQueryHandlerTests
{
    /// <summary>
    /// The property the whole document exists for: a confirmation letter and the statement it
    /// confirms must not be able to disagree. Both read <c>ContactLedgerReader</c>, so this asserts
    /// the wiring, not the arithmetic -- phase-26b's shared-reader lesson, made testable.
    /// </summary>
    [Fact]
    public async Task The_confirmed_balance_equals_the_statements_closing_balance_for_the_same_date()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, customerId) = await SeedCustomerWithAnInvoiceAsync(db);
        var asOf = new DateOnly(2026, 8, 31);

        var statement = await new ContactStatementQueryHandler(db).Handle(
            new ContactStatementQuery(organizationId, ContactType.Customer, customerId, new DateOnly(2026, 1, 1), asOf),
            CancellationToken.None);

        var confirmation = await new PrintBalanceConfirmationQueryHandler(db).Handle(
            new PrintBalanceConfirmationQuery(organizationId, ContactType.Customer, customerId, asOf),
            CancellationToken.None);

        Assert.Equal(statement.ClosingBalance, confirmation.Balance);
        Assert.Equal(statement.ClosingBalanceType, confirmation.BalanceType);

        // 1,000 opening + 2 x 150 invoiced.
        Assert.Equal(1300m, confirmation.Balance);
        Assert.Equal("DR", confirmation.BalanceType);
    }

    /// <summary>A tenant that has never opened Configurations &gt; Custom Templates still gets a
    /// usable letter, with its merge fields filled in.</summary>
    [Fact]
    public async Task A_tenant_with_no_template_gets_the_default_letter_with_merge_fields_substituted()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, customerId) = await SeedCustomerWithAnInvoiceAsync(db);

        var confirmation = await new PrintBalanceConfirmationQueryHandler(db).Handle(
            new PrintBalanceConfirmationQuery(organizationId, ContactType.Customer, customerId, new DateOnly(2026, 8, 31)),
            CancellationToken.None);

        Assert.Equal("Default", confirmation.TemplateName);
        Assert.Contains("Acme Traders", confirmation.Body, StringComparison.Ordinal);
        Assert.Contains("1,300.00", confirmation.Body, StringComparison.Ordinal);
        Assert.Contains("2026-08-31", confirmation.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("$[", confirmation.Body, StringComparison.Ordinal);
    }

    /// <summary>CustomTemplate's first read since Phase 20d created the entity. The tenant's own
    /// wording wins, and its <c>$[placeholder]$</c> fields are filled the same way.</summary>
    [Fact]
    public async Task The_tenants_default_template_replaces_the_built_in_letter()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, customerId) = await SeedCustomerWithAnInvoiceAsync(db);

        db.CustomTemplates.Add(CustomTemplate.Create(
            organizationId,
            "Year-end confirmation",
            CustomTemplateType.CustomerBalanceConfirmation,
            "Namaste $[ContactName]$ — we show $[Balance]$ $[BalanceType]$ as at $[AsOfDate]$.",
            isDefault: true));
        await db.SaveChangesAsync(CancellationToken.None);

        var confirmation = await new PrintBalanceConfirmationQueryHandler(db).Handle(
            new PrintBalanceConfirmationQuery(organizationId, ContactType.Customer, customerId, new DateOnly(2026, 8, 31)),
            CancellationToken.None);

        Assert.Equal("Year-end confirmation", confirmation.TemplateName);
        Assert.Equal("Namaste Acme Traders — we show 1,300.00 DR as at 2026-08-31.", confirmation.Body);
    }

    /// <summary>Phase 27b's other half reaches this letter too: the as-of date it states is the one
    /// the caller's calendar asked for. 2026-08-31 AD is 2083-05-15 BS.</summary>
    [Fact]
    public async Task The_letter_states_its_date_in_the_calendar_the_request_asked_for()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, customerId) = await SeedCustomerWithAnInvoiceAsync(db);

        try
        {
            RequestCalendar.Current = CalendarFormat.Bs;
            var confirmation = await new PrintBalanceConfirmationQueryHandler(db).Handle(
                new PrintBalanceConfirmationQuery(organizationId, ContactType.Customer, customerId, new DateOnly(2026, 8, 31)),
                CancellationToken.None);

            Assert.Equal("2083-05-15", confirmation.AsOfDateText);
            Assert.Contains("2083-05-15", confirmation.Body, StringComparison.Ordinal);
            Assert.Equal("Dates shown in Bikram Sambat (BS)", confirmation.CalendarNote);
        }
        finally
        {
            RequestCalendar.Current = CalendarFormat.Ad;
        }
    }

    /// <summary>Entities are built directly rather than through the Create/Approve command chain:
    /// this handler reads Invoice rows and an opening balance, and routing through the posting and
    /// stock engines would be seeding infrastructure the assertion never touches.</summary>
    private static async Task<(Guid OrganizationId, Guid CustomerId)> SeedCustomerWithAnInvoiceAsync(IAppDbContext db)
    {
        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", "Kathmandu", new DateOnly(2026, 1, 1), true, "moonbeam",
            "info@moonbeam.test", "01-4000000", "PAN12345", null, Guid.NewGuid());
        db.Organizations.Add(organization);

        var customer = Contact.Create(
            organization.Id, ContactType.Customer, "Acme Traders", "C-001", "Pokhara", "PAN99", null, null, null, 1000m);
        db.Contacts.Add(customer);

        var product = Product.Create(
            organization.Id, ProductType.Goods, "Widget", "P-001", Guid.NewGuid(), Guid.NewGuid(), null, true,
            150, 100, VatRate.NoVat, 0, true);
        db.Products.Add(product);

        var invoice = Invoice.Create(organization.Id, customer.Id, Guid.NewGuid(), new DateOnly(2026, 8, 1), null, null, null);
        invoice.AddLine(product.Id, 2, 150, VatRate.NoVat, 0);
        invoice.Approve(Guid.NewGuid(), "INV-001");
        db.Invoices.Add(invoice);

        await db.SaveChangesAsync(CancellationToken.None);
        return (organization.Id, customer.Id);
    }
}
