using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Platform.Queries.GlobalSearch;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Platform;

/// <summary>
/// Phase 33 -- the global search's record half. The point of every test here is the same one:
/// <c>Platform.GlobalSearch.View</c> is a blanket key that discloses nothing on its own, so what a
/// caller finds is decided entirely by the collection keys and location grants they hold.
/// </summary>
public class GlobalSearchTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Master_data_matches_on_name_and_on_code()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(
            db, organizationId, UserId, PermissionKeys.ContactView, PermissionKeys.ProductView);

        db.Contacts.Add(Customer(organizationId, "Himalaya Traders", "CUS0001"));
        db.Products.Add(Service(organizationId, "Consulting", "SRV0042"));
        await db.SaveChangesAsync(CancellationToken.None);

        var byName = await SearchAsync(db, organizationId, "Himalaya");
        var byCode = await SearchAsync(db, organizationId, "SRV0042");

        Assert.Equal("CUS0001", Assert.Single(byName).Code);
        Assert.Equal(GlobalSearchCollection.Contact, byName[0].Collection);
        Assert.Equal("Customer", byName[0].SubKind);

        Assert.Equal("Consulting", Assert.Single(byCode).Name);
        Assert.Equal(GlobalSearchCollection.Product, byCode[0].Collection);
    }

    /// <summary>
    /// The behaviour the confirm-live pass found and the roadmap did not record: a document has no
    /// name, so it is matched on its number alone. Searching the customer's name must not surface
    /// that customer's invoices.
    /// </summary>
    [Fact]
    public async Task A_document_is_found_by_its_number_and_never_by_its_contacts_name()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(
            db, organizationId, UserId, PermissionKeys.ContactView, PermissionKeys.InvoiceView);

        var contact = Customer(organizationId, "Himalaya Traders", "CUS0001");
        db.Contacts.Add(contact);
        db.Invoices.Add(ApprovedInvoice(organizationId, contact.Id, "INV-0007", locationId: null));
        await db.SaveChangesAsync(CancellationToken.None);

        var byNumber = await SearchAsync(db, organizationId, "INV-0007");
        var byContactName = await SearchAsync(db, organizationId, "Himalaya");

        var hit = Assert.Single(byNumber);
        Assert.Equal(GlobalSearchCollection.Document, hit.Collection);
        Assert.Equal(DocumentType.Invoice, hit.DocumentType);
        Assert.Equal("INV-0007", hit.Code);
        Assert.Null(hit.Name);

        // The contact itself, and nothing of its invoices.
        Assert.Equal(GlobalSearchCollection.Contact, Assert.Single(byContactName).Collection);
    }

    [Fact]
    public async Task A_caller_without_the_collections_own_view_key_finds_nothing_of_it()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();

        // Everything except Sales.Invoice.View.
        await PermissionGrantSeed.GrantAsync(db, organizationId, UserId, PermissionKeys.ContactView);

        db.Invoices.Add(ApprovedInvoice(organizationId, Guid.NewGuid(), "INV-0007", locationId: null));
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await SearchAsync(db, organizationId, "INV-0007"));
    }

    /// <summary>
    /// Phase 32b's read side. A grant held only at one location must not turn the search into a
    /// tenant-wide index -- which is exactly what the two pre-32b inlined permission joins this phase
    /// also fixed were doing.
    /// </summary>
    [Fact]
    public async Task A_location_scoped_grant_finds_only_that_locations_documents()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        // Membership only -- no organization-wide InvoiceView at all.
        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organizationId, UserId, MembershipRole.Admin));
        db.RolePermissions.Add(RolePermission.Create(
            Guid.NewGuid(), Role.AdminId, PermissionKeys.InvoiceView, true, locationId: mine));

        db.Invoices.Add(ApprovedInvoice(organizationId, Guid.NewGuid(), "INV-0001", mine));
        db.Invoices.Add(ApprovedInvoice(organizationId, Guid.NewGuid(), "INV-0002", theirs));
        db.Invoices.Add(ApprovedInvoice(organizationId, Guid.NewGuid(), "INV-0003", locationId: null));
        await db.SaveChangesAsync(CancellationToken.None);

        var hits = await SearchAsync(db, organizationId, "INV-000");

        // Their branch's invoice is invisible, and so is the one carrying no location at all --
        // AuthorizationBehavior's own LocationScopeOutcome.NoLocation branch refuses that row too.
        Assert.Equal(["INV-0001"], hits.Select(x => x.Code));
    }

    [Fact]
    public async Task Another_organizations_rows_are_never_returned()
    {
        var db = TestAppDbContext.Create();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(db, mine, UserId, PermissionKeys.ContactView);

        db.Contacts.Add(Customer(theirs, "Himalaya Traders", "CUS0001"));
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await SearchAsync(db, mine, "Himalaya"));
    }

    [Fact]
    public async Task An_inactive_contact_is_not_offered()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(db, organizationId, UserId, PermissionKeys.ContactView);

        var contact = Customer(organizationId, "Himalaya Traders", "CUS0001");
        contact.Deactivate();
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await SearchAsync(db, organizationId, "Himalaya"));
    }

    /// <summary>
    /// A blank term against <c>Contains</c> matches every row in the tenant. The validator refuses
    /// it, and the handler refuses it again -- this pins the second line of defence, since the
    /// handler is what a future non-MediatR caller would reach.
    /// </summary>
    [Fact]
    public async Task A_term_below_the_minimum_length_returns_nothing_even_past_the_validator()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(db, organizationId, UserId, PermissionKeys.ContactView);

        db.Contacts.Add(Customer(organizationId, "Himalaya Traders", "CUS0001"));
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await SearchAsync(db, organizationId, " H "));
    }

    private static async Task<IReadOnlyList<GlobalSearchHitDto>> SearchAsync(
        IAppDbContext db, Guid organizationId, string term) =>
        await new GlobalSearchQueryHandler(db, new FakeCurrentUserService(UserId))
            .Handle(new GlobalSearchQuery(organizationId, term, null), CancellationToken.None);

    private static Contact Customer(Guid organizationId, string name, string code) =>
        Contact.Create(organizationId, ContactType.Customer, name, code, null, null, null, null, null, 0m);

    private static Product Service(Guid organizationId, string name, string code) =>
        Product.Create(
            organizationId, ProductType.Service, name, code, Guid.NewGuid(), Guid.NewGuid(), null,
            availableForSale: true, sellingPrice: 0m, purchasePrice: 0m, vatRate: VatRate.NoVat,
            reOrderLevel: 0, trackInventory: false);

    private static Invoice ApprovedInvoice(Guid organizationId, Guid contactId, string code, Guid? locationId)
    {
        var invoice = Invoice.Create(
            organizationId, contactId, Guid.NewGuid(), new DateOnly(2026, 1, 1), null, null, null);
        invoice.SetLocation(locationId);

        // A real line, because Approve refuses an empty invoice -- and Approve is what assigns the
        // Code this whole feature searches on. A Draft's code is the DRAFT placeholder, so a draft is
        // unfindable by number in the product too, not merely in this test.
        invoice.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.NoVat, 0m);
        invoice.Approve(UserId, code);
        return invoice;
    }
}
