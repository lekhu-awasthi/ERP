using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration;
using ErpApp.Application.Configuration.Commands.ClearPrintingTemplateLocationChoice;
using ErpApp.Application.Configuration.Commands.SetPrintingTemplateLocationChoice;
using ErpApp.Application.Configuration.Commands.UpdatePrintingTemplate;
using ErpApp.Application.Printing;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Printing;

/// <summary>Phase 75 -- a document prints its location, and its location may choose the template it prints with.</summary>
public class PrintingByLocationTests
{
    // ---- Decision A: the location line, only where there is more than one location -------------------

    [Fact]
    public async Task An_organization_with_one_location_prints_no_location_line()
    {
        var seed = await SeedAsync(secondLocation: false);

        var dto = await PrintAsync(seed, seed.Branch.Id);

        Assert.Null(dto.LocationName);
        Assert.Null(dto.LocationAddress);
    }

    [Fact]
    public async Task With_a_second_location_a_document_prints_its_own_location_and_address()
    {
        var seed = await SeedAsync(secondLocation: true);

        var atBranch = await PrintAsync(seed, seed.Branch.Id);
        var atHeadOffice = await PrintAsync(seed, seed.HeadOffice.Id);

        Assert.Equal(("Lakeside", "Pokhara-6"), (atBranch.LocationName, atBranch.LocationAddress));
        Assert.Equal("Kathmandu, Nepal", atBranch.OrganizationAddress);
        Assert.Equal(("HeadOffice", null), (atHeadOffice.LocationName, atHeadOffice.LocationAddress));
    }

    [Fact]
    public async Task A_closed_branch_still_counts_so_its_reprints_keep_their_location()
    {
        var seed = await SeedAsync(secondLocation: true);
        seed.Branch.Update(seed.Branch.Code, seed.Branch.Name, seed.Branch.Address, null, isActive: false);
        await seed.Db.SaveChangesAsync(CancellationToken.None);

        var dto = await PrintAsync(seed, seed.Branch.Id);

        Assert.Equal("Lakeside", dto.LocationName);
    }

    [Fact]
    public async Task A_document_with_no_location_prints_none()
    {
        var seed = await SeedAsync(secondLocation: true);

        var dto = await PrintAsync(seed, locationId: null);

        Assert.Null(dto.LocationName);
    }

    [Fact]
    public async Task A_template_can_hide_the_location_line()
    {
        var seed = await SeedAsync(secondLocation: true);
        var template = AddTemplate(seed, "No location", isDefault: true);
        template.UpdateLayout(PrintLayout.Standard, [PrintElement.Location], [], null, false);
        await seed.Db.SaveChangesAsync(CancellationToken.None);

        var dto = await PrintAsync(seed, seed.Branch.Id);

        Assert.Null(dto.LocationName);
        Assert.Null(dto.LocationAddress);
    }

    [Fact]
    public void The_location_is_offered_on_every_printable_type_and_locked_on_none()
    {
        foreach (var type in DocumentMechanisms.Printable)
        {
            Assert.True(PrintElements.CanHide(type, PrintElement.Location), type.ToString());
        }
    }

    // ---- Decision B: a location's own template, falling back to the default ---------------------------

    [Fact]
    public async Task A_location_with_a_choice_prints_with_it_and_the_others_with_the_default()
    {
        var seed = await SeedAsync(secondLocation: true);
        AddTemplate(seed, "Plain", isDefault: true);
        var ruled = AddTemplate(seed, "Ruled", isDefault: false, PrintLayout.Traditional);
        await seed.Db.SaveChangesAsync(CancellationToken.None);
        await EveryTransactionCarriesALocationAsync(seed);
        await SetAsync(seed, seed.Branch.Id, ruled.Id);

        Assert.Equal(("Ruled", PrintLayout.Traditional), Of(await PrintAsync(seed, seed.Branch.Id)));
        Assert.Equal(("Plain", PrintLayout.Standard), Of(await PrintAsync(seed, seed.HeadOffice.Id)));
        Assert.Equal(("Plain", PrintLayout.Standard), Of(await PrintAsync(seed, locationId: null)));
    }

    [Fact]
    public async Task With_no_default_a_location_without_a_choice_prints_as_before_any_template()
    {
        var seed = await SeedAsync(secondLocation: true);
        var ruled = AddTemplate(seed, "Ruled", isDefault: false, PrintLayout.Traditional);
        await seed.Db.SaveChangesAsync(CancellationToken.None);
        await EveryTransactionCarriesALocationAsync(seed);
        await SetAsync(seed, seed.Branch.Id, ruled.Id);

        Assert.Equal(("Default", PrintLayout.Standard), Of(await PrintAsync(seed, seed.HeadOffice.Id)));
    }

    [Fact]
    public async Task A_stale_choice_naming_an_inactive_or_retyped_template_falls_back_to_the_default()
    {
        // Rows written by some other door than the commands, which would have removed them: the resolver re-checks.
        var seed = await SeedAsync(secondLocation: true);
        AddTemplate(seed, "Plain", isDefault: true);
        var ruled = AddTemplate(seed, "Ruled", isDefault: false, PrintLayout.Traditional);
        await seed.Db.SaveChangesAsync(CancellationToken.None);
        seed.Db.PrintingTemplateLocationChoices.Add(PrintingTemplateLocationChoice.Create(seed.OrganizationId, seed.Branch.Id, ruled));
        ruled.Update("Ruled", DocumentType.Invoice, isActive: true);
        await seed.Db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("Plain", (await PrintAsync(seed, seed.Branch.Id)).PrintingTemplateName);

        ruled.Update("Ruled", DocumentType.Quotation, isActive: false);
        await seed.Db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("Plain", (await PrintAsync(seed, seed.Branch.Id)).PrintingTemplateName);
    }

    // ---- The commands -----------------------------------------------------------------------------

    [Fact]
    public async Task Setting_twice_replaces_the_choice_and_clearing_returns_the_location_to_the_default()
    {
        var seed = await SeedAsync(secondLocation: true);
        var first = AddTemplate(seed, "First", isDefault: false, documentType: DocumentType.Invoice);
        var second = AddTemplate(seed, "Second", isDefault: false, documentType: DocumentType.Invoice);
        await seed.Db.SaveChangesAsync(CancellationToken.None);

        await SetAsync(seed, seed.Branch.Id, first.Id);
        var result = await SetAsync(seed, seed.Branch.Id, second.Id);

        Assert.Equal(new PrintingTemplateLocationChoiceDto(seed.Branch.Id, DocumentType.Invoice, second.Id), result);
        Assert.Equal(second.Id, Assert.Single(await seed.Db.PrintingTemplateLocationChoices.ToListAsync()).PrintingTemplateId);

        await ClearAsync(seed, seed.Branch.Id, DocumentType.Invoice);
        Assert.Empty(await seed.Db.PrintingTemplateLocationChoices.ToListAsync());

        // Clearing a location that has no choice is already the state asked for.
        await ClearAsync(seed, seed.Branch.Id, DocumentType.Invoice);
    }

    [Fact]
    public async Task A_type_carrying_no_location_here_is_refused_rather_than_stored_and_never_read()
    {
        // Sales-only scope (the default): a Quotation carries no location, so a choice for it could never apply.
        var seed = await SeedAsync(secondLocation: true);
        var template = AddTemplate(seed, "Quote", isDefault: false);
        await seed.Db.SaveChangesAsync(CancellationToken.None);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => SetAsync(seed, seed.Branch.Id, template.Id));

        Assert.Contains("carries no location", refused.Message);
        Assert.Empty(await seed.Db.PrintingTemplateLocationChoices.ToListAsync());
    }

    [Fact]
    public async Task An_inactive_template_is_refused()
    {
        var seed = await SeedAsync(secondLocation: true);
        var template = AddTemplate(seed, "Old", isDefault: false, documentType: DocumentType.Invoice);
        template.Update("Old", DocumentType.Invoice, isActive: false);
        await seed.Db.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => SetAsync(seed, seed.Branch.Id, template.Id));
    }

    [Fact]
    public async Task Another_organizations_location_or_template_is_not_found()
    {
        var seed = await SeedAsync(secondLocation: true);
        var other = await SeedAsync(secondLocation: true);
        var template = AddTemplate(seed, "Mine", isDefault: false, documentType: DocumentType.Invoice);
        var theirs = AddTemplate(other, "Theirs", isDefault: false, documentType: DocumentType.Invoice);
        await seed.Db.SaveChangesAsync(CancellationToken.None);
        await other.Db.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() => SetAsync(seed, other.Branch.Id, template.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => SetAsync(seed, seed.Branch.Id, theirs.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => ClearAsync(seed, other.Branch.Id, DocumentType.Invoice));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retyping_or_deactivating_a_template_sends_its_locations_back_to_the_default(bool retype)
    {
        var seed = await SeedAsync(secondLocation: true);
        var template = AddTemplate(seed, "Branch bill", isDefault: false, documentType: DocumentType.Invoice);
        var kept = AddTemplate(seed, "Kept", isDefault: false, documentType: DocumentType.CreditNote);
        await seed.Db.SaveChangesAsync(CancellationToken.None);
        await SetAsync(seed, seed.Branch.Id, template.Id);
        await SetAsync(seed, seed.Branch.Id, kept.Id);

        await new UpdatePrintingTemplateCommandHandler(seed.Db).Handle(
            new UpdatePrintingTemplateCommand(
                seed.OrganizationId, template.Id, "Branch bill",
                retype ? DocumentType.SalesOrder : DocumentType.Invoice, IsActive: retype),
            CancellationToken.None);

        var left = Assert.Single(await seed.Db.PrintingTemplateLocationChoices.ToListAsync());
        Assert.Equal(kept.Id, left.PrintingTemplateId);
    }

    [Fact]
    public async Task Renaming_a_template_keeps_its_location_choices()
    {
        var seed = await SeedAsync(secondLocation: true);
        var template = AddTemplate(seed, "Branch bill", isDefault: false, documentType: DocumentType.Invoice);
        await seed.Db.SaveChangesAsync(CancellationToken.None);
        await SetAsync(seed, seed.Branch.Id, template.Id);

        await new UpdatePrintingTemplateCommandHandler(seed.Db).Handle(
            new UpdatePrintingTemplateCommand(seed.OrganizationId, template.Id, "Lakeside bill", DocumentType.Invoice, true),
            CancellationToken.None);

        Assert.Single(await seed.Db.PrintingTemplateLocationChoices.ToListAsync());
    }

    [Fact]
    public void A_choice_cannot_name_another_types_template()
    {
        var organizationId = Guid.NewGuid();
        var invoice = PrintingTemplate.Create(organizationId, "Invoice", DocumentType.Invoice, false);
        var quotation = PrintingTemplate.Create(organizationId, "Quotation", DocumentType.Quotation, false);
        var choice = PrintingTemplateLocationChoice.Create(organizationId, Guid.NewGuid(), invoice);

        Assert.Throws<InvalidOperationException>(() => choice.Choose(quotation));
        Assert.Throws<InvalidOperationException>(
            () => PrintingTemplateLocationChoice.Create(Guid.NewGuid(), Guid.NewGuid(), invoice));
    }

    // ---- Helpers --------------------------------------------------------------------------------------

    private sealed record Seed(IAppDbContext Db, Guid OrganizationId, Guid ContactId, Guid ProductId, BillingLocation HeadOffice, BillingLocation Branch);

    private static (string Name, PrintLayout Layout) Of(PrintableDocumentDto dto) => (dto.PrintingTemplateName, dto.Layout);

    /// <summary>An organization with its HeadOffice and, optionally, a branch with an address. With no second location
    /// the "branch" is the HeadOffice itself.</summary>
    private static async Task<Seed> SeedAsync(bool secondLocation)
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", "Kathmandu, Nepal", new DateOnly(2026, 1, 1), true, "moonbeam",
            "info@moonbeam.test", "01-4000000", "PAN12345", "https://moonbeam.test", Guid.NewGuid());
        db.Organizations.Add(organization);
        db.TenantSettings.Add(TenantSettings.CreateDefault(organization.Id));

        var headOffice = BillingLocation.CreateHeadOffice(organization.Id);
        db.BillingLocations.Add(headOffice);
        var branch = headOffice;
        if (secondLocation)
        {
            branch = BillingLocation.Create(organization.Id, "1002", "Lakeside", "Pokhara-6", null);
            db.BillingLocations.Add(branch);
        }

        var pieces = UnitOfMeasurement.Create(organization.Id, "Pieces", "Pcs");
        var contact = Contact.Create(organization.Id, ContactType.Customer, "Acme Traders", "C-001", null, null, null, null, null, 0);
        var product = Product.Create(
            organization.Id, ProductType.Service, "Widget", "P-001", Guid.NewGuid(), pieces.Id, null, true, 100, 80, VatRate.NoVat, 0, false);
        db.UnitsOfMeasurement.Add(pieces);
        db.Contacts.Add(contact);
        db.Products.Add(product);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(db, organization.Id, contact.Id, product.Id, headOffice, branch);
    }

    private static PrintingTemplate AddTemplate(
        Seed seed, string name, bool isDefault, PrintLayout layout = PrintLayout.Standard,
        DocumentType documentType = DocumentType.Quotation)
    {
        var template = PrintingTemplate.Create(seed.OrganizationId, name, documentType, isDefault);
        template.UpdateLayout(layout, [], [], null, false);
        seed.Db.PrintingTemplates.Add(template);
        return template;
    }

    /// <summary>A quotation at the given location, printed. Quotations print without a counted copy, so the handler can
    /// be called directly; its location is stored whatever the scope mode, which is what the reader reads.</summary>
    private static async Task<PrintableDocumentDto> PrintAsync(Seed seed, Guid? locationId)
    {
        var quotation = Quotation.Create(seed.OrganizationId, seed.ContactId, new DateOnly(2026, 8, 1), null, null);
        quotation.AddLine(seed.ProductId, 1, 100, VatRate.NoVat, 0, null, 1m);
        quotation.SetLocation(locationId);
        seed.Db.Quotations.Add(quotation);
        await seed.Db.SaveChangesAsync(CancellationToken.None);

        return await new PrintDocumentQueryHandler(seed.Db, new FakeFileStorage()).Handle(
            new PrintDocumentQuery(seed.OrganizationId, DocumentType.Quotation, quotation.Id), CancellationToken.None);
    }

    /// <summary>The print tests print Quotations, which carry a location only when every transaction does.</summary>
    private static async Task EveryTransactionCarriesALocationAsync(Seed seed)
    {
        var settings = await seed.Db.TenantSettings.SingleAsync(x => x.OrganizationId == seed.OrganizationId);
        settings.SetLocationSettings(LocationScopeMode.AllTransactions, false);
        await seed.Db.SaveChangesAsync(CancellationToken.None);
    }

    private static Task<PrintingTemplateLocationChoiceDto> SetAsync(Seed seed, Guid locationId, Guid templateId) =>
        new SetPrintingTemplateLocationChoiceCommandHandler(seed.Db).Handle(
            new SetPrintingTemplateLocationChoiceCommand(seed.OrganizationId, locationId, templateId), CancellationToken.None);

    private static Task ClearAsync(Seed seed, Guid locationId, DocumentType documentType) =>
        new ClearPrintingTemplateLocationChoiceCommandHandler(seed.Db).Handle(
            new ClearPrintingTemplateLocationChoiceCommand(seed.OrganizationId, locationId, documentType), CancellationToken.None);
}
