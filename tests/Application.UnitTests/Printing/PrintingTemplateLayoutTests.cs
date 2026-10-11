using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.UpdatePrintingTemplateLayout;
using ErpApp.Application.Printing;
using ErpApp.Application.Printing.Queries.ListPrintElements;
using ErpApp.Application.Printing.Queries.PreviewPrintingTemplate;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using FluentValidation;

namespace ErpApp.Application.UnitTests.Printing;

/// <summary>Phase 73 -- a printing template's layout, what it hides, and what a print does with it.</summary>
public class PrintingTemplateLayoutTests
{
    // ---- The applier: one pure function for a print and a preview ----------------------------------

    [Fact]
    public void Applier_drops_a_hidden_column_from_the_header_every_row_and_the_total()
    {
        var dto = SampleDto(DocumentType.Quotation);
        var settings = Settings(hidden: [PrintElement.VatColumn, PrintElement.DiscountColumn]);

        var printed = PrintTemplateApplier.Apply(dto, settings);

        var items = Assert.Single(printed.Sections);
        Assert.Equal(["Product", "Qty", "Unit", "Rate", "Amount"], items.Columns.Select(x => x.Header));
        Assert.Equal(["Widget", "2", "Pcs", "100.00", "226.00"], items.Rows.Single().Cells);
        Assert.Equal(["Total", "2", "Pcs", "", "226.00"], items.TotalRow!.Cells);
    }

    [Fact]
    public void Applier_hides_organization_and_party_fields_and_carries_the_layout_through()
    {
        var dto = SampleDto(DocumentType.Quotation);
        var settings = Settings(
            hidden: [PrintElement.OrganizationPan, PrintElement.OrganizationLogo, PrintElement.PartyAddress,
                     PrintElement.Reference, PrintElement.AmountInWords, PrintElement.SignatureLine, PrintElement.Terms],
            layout: PrintLayout.Traditional, footer: "Bank: NIC Asia 001", compact: true);

        var printed = PrintTemplateApplier.Apply(dto, settings);

        Assert.Null(printed.OrganizationPan);
        Assert.Null(printed.OrganizationLogo);
        Assert.Null(printed.PartyAddress);
        Assert.Null(printed.Reference);
        Assert.Null(printed.AmountInWords);
        Assert.Null(printed.Terms);
        Assert.False(printed.ShowSignatureLine);
        Assert.Equal("Kathmandu", printed.OrganizationAddress);
        Assert.Equal(PrintLayout.Traditional, printed.Layout);
        Assert.Equal("Bank: NIC Asia 001", printed.FooterText);
        Assert.True(printed.CompactMargins);
    }

    [Fact]
    public void Applier_never_hides_a_tax_documents_statutory_content_whatever_the_settings_say()
    {
        // A row written by some other door than UpdateLayout: the applier re-checks rather than trusting it.
        var dto = SampleDto(DocumentType.Invoice);
        var settings = Settings(hidden: [PrintElement.OrganizationPan, PrintElement.OrganizationAddress, PrintElement.PartyAddress, PrintElement.VatColumn]);

        var printed = PrintTemplateApplier.Apply(dto, settings);

        Assert.Equal("PAN12345", printed.OrganizationPan);
        Assert.Equal("Kathmandu", printed.OrganizationAddress);
        Assert.Equal("Pokhara", printed.PartyAddress);
        Assert.DoesNotContain(printed.Sections.Single().Columns, x => x.Header == "VAT");
    }

    [Fact]
    public void Applier_with_the_default_settings_changes_nothing_it_prints()
    {
        var dto = SampleDto(DocumentType.Quotation);

        var printed = PrintTemplateApplier.Apply(dto, PrintingTemplateSettings.Default);

        Assert.Equal(dto.Sections.Single().Columns, printed.Sections.Single().Columns);
        Assert.Equal(dto.OrganizationPan, printed.OrganizationPan);
        Assert.Equal(dto.AmountInWords, printed.AmountInWords);
        Assert.True(printed.ShowSignatureLine);
        Assert.Equal(PrintLayout.Standard, printed.Layout);
        Assert.Null(printed.FooterText);
    }

    // ---- A real print reads the type's default template --------------------------------------------

    [Fact]
    public async Task A_print_prints_the_unit_and_the_amount_in_words_and_no_empty_hs_code_column()
    {
        var (db, organizationId, quotation, _) = await SeedQuotationAsync(hsCode: null);

        var dto = await Print(db, organizationId, quotation.Id);

        var items = dto.Sections.Single();
        Assert.Equal(["Product", "Qty", "Unit", "Rate", "Disc %", "VAT", "Amount"], items.Columns.Select(x => x.Header));
        Assert.Equal("Box", items.Rows[0].Cells[2]);
        Assert.Equal("Pcs", items.Rows[1].Cells[2]);
        // Two units: a quantity total would add a box to a piece.
        Assert.Equal("", items.TotalRow!.Cells[1]);
        Assert.Equal("Rupees Six Hundred Only", dto.AmountInWords);
        Assert.Equal(PrintLayout.Standard, dto.Layout);
    }

    [Fact]
    public async Task A_print_shows_the_hs_code_column_once_a_line_has_one()
    {
        var (db, organizationId, quotation, _) = await SeedQuotationAsync(hsCode: "8471");

        var dto = await Print(db, organizationId, quotation.Id);

        var items = dto.Sections.Single();
        Assert.Equal("HS Code", items.Columns[1].Header);
        Assert.Equal("8471", items.Rows[0].Cells[1]);
    }

    [Fact]
    public async Task A_print_follows_the_default_template_hiding_columns_codes_and_printing_custom_fields()
    {
        var (db, organizationId, quotation, product) = await SeedQuotationAsync(hsCode: null);
        var site = CustomFieldDefinition.Create(organizationId, "Site", CustomFieldType.Text, [DocumentType.Quotation], []);
        var blank = CustomFieldDefinition.Create(organizationId, "Vehicle", CustomFieldType.Text, [DocumentType.Quotation], []);
        var po = CustomFieldDefinition.Create(organizationId, "Buyer PO", CustomFieldType.Text, [DocumentType.Quotation], []);
        db.CustomFieldDefinitions.AddRange(site, blank, po);
        db.CustomFieldValues.AddRange(
            CustomFieldValue.Create(organizationId, site.Id, DocumentType.Quotation, quotation.Id, "Butwal depot", CustomFieldType.Text),
            CustomFieldValue.Create(organizationId, po.Id, DocumentType.Quotation, quotation.Id, "PO-77", CustomFieldType.Text));

        var template = PrintingTemplate.Create(organizationId, "Plain", DocumentType.Quotation, isDefault: true);
        template.UpdateLayout(
            PrintLayout.Minimal, [PrintElement.VatColumn, PrintElement.ItemCode], [po.Id, blank.Id, site.Id], "Thank you", false);
        db.PrintingTemplates.Add(template);
        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, quotation.Id);

        var items = dto.Sections.Single();
        Assert.DoesNotContain(items.Columns, x => x.Header == "VAT");
        Assert.Equal(product.Name, items.Rows[1].Cells[0]);
        // The template's order, and a field left blank on this document prints nothing.
        Assert.Equal([("Buyer PO", "PO-77"), ("Site", "Butwal depot")], dto.CustomFields!.Select(x => (x.Label, x.Value)));
        Assert.Equal(PrintLayout.Minimal, dto.Layout);
        Assert.Equal("Thank you", dto.FooterText);
        Assert.Equal("Plain", dto.PrintingTemplateName);
    }

    [Fact]
    public async Task A_custom_field_switched_off_since_the_template_was_saved_prints_nothing()
    {
        var (db, organizationId, quotation, _) = await SeedQuotationAsync(hsCode: null);
        var site = CustomFieldDefinition.Create(organizationId, "Site", CustomFieldType.Text, [DocumentType.Quotation], []);
        db.CustomFieldDefinitions.Add(site);
        db.CustomFieldValues.Add(
            CustomFieldValue.Create(organizationId, site.Id, DocumentType.Quotation, quotation.Id, "Butwal", CustomFieldType.Text));
        var template = PrintingTemplate.Create(organizationId, "Plain", DocumentType.Quotation, isDefault: true);
        template.UpdateLayout(PrintLayout.Standard, [], [site.Id], null, false);
        db.PrintingTemplates.Add(template);
        site.Update("Site", CustomFieldType.Text, [DocumentType.Quotation], isActive: false, []);
        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await Print(db, organizationId, quotation.Id);

        Assert.Empty(dto.CustomFields!);
    }

    [Fact]
    public async Task A_foreign_currency_document_prints_its_amount_in_words_in_its_own_currency()
    {
        // Phase 79 -- phase 73's Decision F printed none here; the words now name the document's currency.
        var (db, organizationId, quotation, _) = await SeedQuotationAsync(hsCode: null, currency: "USD");

        var dto = await Print(db, organizationId, quotation.Id);

        Assert.Equal("US Dollars Six Hundred Only", dto.AmountInWords);
    }

    // ---- The save ------------------------------------------------------------------------------------

    [Fact]
    public async Task Save_stores_the_settings()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var site = CustomFieldDefinition.Create(organizationId, "Site", CustomFieldType.Text, [DocumentType.Invoice], []);
        var template = PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true);
        db.CustomFieldDefinitions.Add(site);
        db.PrintingTemplates.Add(template);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new UpdatePrintingTemplateLayoutCommandHandler(db).Handle(
            new UpdatePrintingTemplateLayoutCommand(
                organizationId, template.Id, PrintLayout.Traditional, [PrintElement.VatColumn], [site.Id], " Thanks ", true),
            CancellationToken.None);

        Assert.Equal(PrintLayout.Traditional, result.Layout);
        Assert.Equal([PrintElement.VatColumn], result.HiddenElements);
        Assert.Equal([site.Id], result.PrintedCustomFieldIds);
        Assert.Equal("Thanks", result.FooterText);

        var stored = db.PrintingTemplates.Single();
        Assert.Equal(PrintLayout.Traditional, stored.Layout);
        Assert.True(stored.CompactMargins);
    }

    [Fact]
    public async Task Save_refuses_hiding_an_invoices_pan_as_a_400_naming_the_field()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var template = PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true);
        db.PrintingTemplates.Add(template);
        await db.SaveChangesAsync(CancellationToken.None);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => new UpdatePrintingTemplateLayoutCommandHandler(db).Handle(
            new UpdatePrintingTemplateLayoutCommand(
                organizationId, template.Id, PrintLayout.Standard, [PrintElement.OrganizationPan], [], null, false),
            CancellationToken.None));

        var failure = Assert.Single(refused.Errors);
        Assert.Equal("HiddenElements", failure.PropertyName);
        Assert.Contains("cannot be hidden", failure.ErrorMessage);
        Assert.Empty(db.PrintingTemplates.Single().HiddenElements);
    }

    [Fact]
    public async Task Save_refuses_a_custom_field_that_does_not_apply_to_the_type_or_is_not_here()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var quotationOnly = CustomFieldDefinition.Create(organizationId, "Site", CustomFieldType.Text, [DocumentType.Quotation], []);
        var elsewhere = CustomFieldDefinition.Create(Guid.NewGuid(), "Theirs", CustomFieldType.Text, [DocumentType.Invoice], []);
        var template = PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true);
        db.CustomFieldDefinitions.AddRange(quotationOnly, elsewhere);
        db.PrintingTemplates.Add(template);
        await db.SaveChangesAsync(CancellationToken.None);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => new UpdatePrintingTemplateLayoutCommandHandler(db).Handle(
            new UpdatePrintingTemplateLayoutCommand(
                organizationId, template.Id, PrintLayout.Standard, [], [quotationOnly.Id, elsewhere.Id], null, false),
            CancellationToken.None));

        Assert.Equal(2, refused.Errors.Count());
        Assert.All(refused.Errors, x => Assert.Equal("PrintedCustomFieldIds", x.PropertyName));
        Assert.Contains(refused.Errors, x => x.ErrorMessage.Contains("does not apply to Invoice"));
    }

    [Fact]
    public async Task Save_of_another_organizations_template_is_a_404()
    {
        var db = TestAppDbContext.Create();
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Standard", DocumentType.Invoice, isDefault: true);
        db.PrintingTemplates.Add(template);
        await db.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() => new UpdatePrintingTemplateLayoutCommandHandler(db).Handle(
            new UpdatePrintingTemplateLayoutCommand(Guid.NewGuid(), template.Id, PrintLayout.Standard, [], [], null, false),
            CancellationToken.None));
    }

    // ---- The validators (built here, so a bad rule fails a test, not every endpoint) -----------------

    [Fact]
    public void The_save_validator_refuses_a_missing_list_or_layout_by_name()
    {
        var validator = new UpdatePrintingTemplateLayoutCommandValidator();

        var result = validator.Validate(new UpdatePrintingTemplateLayoutCommand(
            Guid.NewGuid(), Guid.NewGuid(), null, null, null, new string('x', PrintingTemplate.FooterTextMaxLength + 1), null));

        Assert.Equal(
            ["CompactMargins", "FooterText", "HiddenElements", "Layout", "PrintedCustomFieldIds"],
            result.Errors.Select(x => x.PropertyName).Distinct().OrderBy(x => x));
        Assert.True(validator.Validate(new UpdatePrintingTemplateLayoutCommand(
            Guid.NewGuid(), Guid.NewGuid(), PrintLayout.Minimal, [], [], null, false)).IsValid);
    }

    [Fact]
    public void The_preview_validator_refuses_a_type_with_nothing_to_print()
    {
        var validator = new PreviewPrintingTemplateQueryValidator();

        var result = validator.Validate(new PreviewPrintingTemplateQuery(
            Guid.NewGuid(), DocumentType.OpeningBalance, PrintLayout.Standard, [], [], null, false));

        Assert.Equal("DocumentType", Assert.Single(result.Errors).PropertyName);
        Assert.True(validator.Validate(new PreviewPrintingTemplateQuery(
            Guid.NewGuid(), DocumentType.JournalVoucher, PrintLayout.Standard, [], [], null, false)).IsValid);
    }

    // ---- The preview ---------------------------------------------------------------------------------

    [Fact]
    public async Task Preview_draws_a_sample_under_the_real_header_with_the_unsaved_settings()
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        var site = CustomFieldDefinition.Create(organization.Id, "Site", CustomFieldType.Text, [DocumentType.Quotation], []);
        db.CustomFieldDefinitions.Add(site);
        await db.SaveChangesAsync(CancellationToken.None);

        var dto = await new PreviewPrintingTemplateQueryHandler(db, new FakeFileStorage()).Handle(
            new PreviewPrintingTemplateQuery(
                organization.Id, DocumentType.Quotation, PrintLayout.Traditional,
                [PrintElement.HsCode, PrintElement.ItemCode, PrintElement.OrganizationPan], [site.Id], "Thanks", false),
            CancellationToken.None);

        Assert.Equal("SAMPLE", dto.Code);
        Assert.Equal("Moonbeam Trading", dto.OrganizationName);
        Assert.Null(dto.OrganizationPan);
        Assert.Equal(PrintLayout.Traditional, dto.Layout);
        var items = dto.Sections.Single();
        Assert.DoesNotContain(items.Columns, x => x.Header == "HS Code");
        Assert.Equal("Sample Product", items.Rows[0].Cells[0]);
        Assert.Equal(("Site", "Sample value"), (dto.CustomFields!.Single().Label, dto.CustomFields!.Single().Value));
        Assert.Equal("Rupees Three Thousand Two Hundred Seventy Seven Only", dto.AmountInWords);
    }

    [Fact]
    public async Task Preview_refuses_hiding_a_tax_documents_statutory_content()
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        await db.SaveChangesAsync(CancellationToken.None);

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            new PreviewPrintingTemplateQueryHandler(db, new FakeFileStorage()).Handle(
                new PreviewPrintingTemplateQuery(
                    organization.Id, DocumentType.CreditNote, PrintLayout.Standard, [PrintElement.PartyAddress], [], null, false),
                CancellationToken.None));

        Assert.Equal("HiddenElements", Assert.Single(refused.Errors).PropertyName);
    }

    // ---- Phase 79: Modern, Classic and the 80 mm roll --------------------------------------------------

    [Fact]
    public void Applier_on_the_roll_drops_the_logo_and_the_signature_line_whatever_the_template_hides()
    {
        var printed = PrintTemplateApplier.Apply(SampleDto(DocumentType.Invoice), Settings(hidden: [], layout: PrintLayout.Retail));

        Assert.Null(printed.OrganizationLogo);
        Assert.False(printed.ShowSignatureLine);
        Assert.Equal(PrintLayout.Retail, printed.Layout);
        // The roll keeps every column: it stacks them, it does not drop them.
        Assert.Equal(7, printed.Sections.Single().Columns.Count);
        Assert.Equal("PAN12345", printed.OrganizationPan);
    }

    [Theory]
    [InlineData(PrintLayout.Modern)]
    [InlineData(PrintLayout.Classic)]
    public void Applier_on_the_new_a4_layouts_keeps_the_logo_and_the_signature_line(PrintLayout layout)
    {
        var printed = PrintTemplateApplier.Apply(SampleDto(DocumentType.Quotation), Settings(hidden: [], layout: layout));

        Assert.NotNull(printed.OrganizationLogo);
        Assert.True(printed.ShowSignatureLine);
    }

    [Fact]
    public async Task Save_stores_the_roll_on_an_invoice()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var template = PrintingTemplate.Create(organizationId, "Roll", DocumentType.Invoice, isDefault: true);
        db.PrintingTemplates.Add(template);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new UpdatePrintingTemplateLayoutCommandHandler(db).Handle(
            new UpdatePrintingTemplateLayoutCommand(organizationId, template.Id, PrintLayout.Retail, [], [], null, false),
            CancellationToken.None);

        Assert.Equal(PrintLayout.Retail, result.Layout);
        Assert.Equal(PrintLayout.Retail, db.PrintingTemplates.Single().Layout);
    }

    [Fact]
    public async Task Save_refuses_the_roll_on_a_quotation_as_a_400_naming_the_layout()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var template = PrintingTemplate.Create(organizationId, "Quote", DocumentType.Quotation, isDefault: true);
        db.PrintingTemplates.Add(template);
        await db.SaveChangesAsync(CancellationToken.None);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => new UpdatePrintingTemplateLayoutCommandHandler(db).Handle(
            new UpdatePrintingTemplateLayoutCommand(organizationId, template.Id, PrintLayout.Retail, [], [], null, false),
            CancellationToken.None));

        var failure = Assert.Single(refused.Errors);
        Assert.Equal("Layout", failure.PropertyName);
        Assert.Contains("not offered for Quotation", failure.ErrorMessage);
        Assert.Equal(PrintLayout.Standard, db.PrintingTemplates.Single().Layout);
    }

    [Fact]
    public async Task Preview_on_the_roll_draws_no_signature_line_and_refuses_a_type_not_offered_it()
    {
        var db = TestAppDbContext.Create();
        var organization = NewOrganization(db);
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new PreviewPrintingTemplateQueryHandler(db, new FakeFileStorage());

        var roll = await handler.Handle(
            new PreviewPrintingTemplateQuery(organization.Id, DocumentType.SalesOrder, PrintLayout.Retail, [], [], null, false),
            CancellationToken.None);

        Assert.Equal(PrintLayout.Retail, roll.Layout);
        Assert.False(roll.ShowSignatureLine);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new PreviewPrintingTemplateQuery(organization.Id, DocumentType.PurchaseBill, PrintLayout.Retail, [], [], null, false),
            CancellationToken.None));
        Assert.Equal("Layout", Assert.Single(refused.Errors).PropertyName);
    }

    [Fact]
    public async Task The_element_table_lists_each_types_layouts_and_what_each_leaves_out()
    {
        var types = await new ListPrintElementsQueryHandler().Handle(new ListPrintElementsQuery(Guid.NewGuid()), CancellationToken.None);

        var invoice = types.Single(x => x.DocumentType == DocumentType.Invoice);
        Assert.Equal(
            [PrintLayout.Standard, PrintLayout.Traditional, PrintLayout.Minimal, PrintLayout.Modern, PrintLayout.Classic, PrintLayout.Retail],
            invoice.Layouts.Select(x => x.Layout));
        Assert.Equal([PrintElement.OrganizationLogo, PrintElement.SignatureLine], invoice.Layouts.Single(x => x.Layout == PrintLayout.Retail).Omits);
        Assert.Equal([PrintElement.OrganizationLogo], invoice.Layouts.Single(x => x.Layout == PrintLayout.Minimal).Omits);
        Assert.Empty(invoice.Layouts.Single(x => x.Layout == PrintLayout.Classic).Omits);

        var bill = types.Single(x => x.DocumentType == DocumentType.PurchaseBill);
        Assert.DoesNotContain(bill.Layouts, x => x.Layout == PrintLayout.Retail);
    }

    // ---- Helpers --------------------------------------------------------------------------------------

    private static PrintingTemplateSettings Settings(
        IReadOnlyList<PrintElement> hidden, PrintLayout layout = PrintLayout.Standard, string? footer = null, bool compact = false) =>
        new("Test", layout, hidden, [], footer, compact);

    private static PrintableDocumentDto SampleDto(DocumentType type) =>
        new(
            type, "Quotation", "Q-1", "2026-08-01", "REF-1", "Moonbeam Trading", "Kathmandu", "01-4000000",
            "info@moonbeam.test", "PAN12345", "https://moonbeam.test", [1, 2, 3], "For", "C-001 — Acme", "Pokhara",
            "Default", [],
            [
                new PrintableSectionDto(
                    "Items",
                    [
                        new PrintableColumnDto("Product", 4),
                        new PrintableColumnDto("Qty", 1, AlignRight: true),
                        new PrintableColumnDto("Unit", 1, Element: PrintElement.Unit),
                        new PrintableColumnDto("Rate", 1, AlignRight: true),
                        new PrintableColumnDto("Disc %", 1, AlignRight: true, Element: PrintElement.DiscountColumn),
                        new PrintableColumnDto("VAT", 1, AlignRight: true, Element: PrintElement.VatColumn),
                        new PrintableColumnDto("Amount", 1, AlignRight: true),
                    ],
                    [new PrintableRowDto(["Widget", "2", "Pcs", "100.00", "0", "26.00", "226.00"])],
                    new PrintableRowDto(["Total", "2", "Pcs", "", "", "26.00", "226.00"])),
            ],
            [new PrintableFieldDto("Grand Total", "226.00", Emphasise: true)],
            null, "<p>Terms</p>", null)
        {
            AmountInWords = "Rupees Two Hundred Twenty Six Only",
        };

    private static async Task<(IAppDbContext Db, Guid OrganizationId, Quotation Quotation, Product Product)> SeedQuotationAsync(
        string? hsCode, string? currency = null)
    {
        var db = TestAppDbContext.Create();
        var organizationId = NewOrganization(db).Id;

        var box = UnitOfMeasurement.Create(organizationId, "Box", "Box");
        var pieces = UnitOfMeasurement.Create(organizationId, "Pieces", "Pcs");
        var contact = Contact.Create(organizationId, ContactType.Customer, "Acme Traders", "C-001", null, null, null, null, null, 0);
        var product = Product.Create(
            organizationId, ProductType.Service, "Widget", "P-001", Guid.NewGuid(), pieces.Id, hsCode, true, 100, 80, VatRate.NoVat, 0, false);
        db.UnitsOfMeasurement.AddRange(box, pieces);
        db.Contacts.Add(contact);
        db.Products.Add(product);

        var quotation = Quotation.Create(organizationId, contact.Id, new DateOnly(2026, 8, 1), null, null);
        // The first line in a box of 12, the second in the product's own (primary) unit.
        quotation.AddLine(product.Id, 1, 500, VatRate.NoVat, 0, box.Id, 12m);
        quotation.AddLine(product.Id, 1, 100, VatRate.NoVat, 0, null, 1m);
        if (currency is not null)
        {
            quotation.SetCurrency(currency, 133.5m);
        }

        db.Quotations.Add(quotation);
        await db.SaveChangesAsync(CancellationToken.None);

        return (db, organizationId, quotation, product);
    }

    private static async Task<PrintableDocumentDto> Print(IAppDbContext db, Guid organizationId, Guid quotationId) =>
        await new PrintDocumentQueryHandler(db, new FakeFileStorage()).Handle(
            new PrintDocumentQuery(organizationId, DocumentType.Quotation, quotationId), CancellationToken.None);

    private static Organization NewOrganization(IAppDbContext db)
    {
        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", "Kathmandu, Nepal", new DateOnly(2026, 1, 1), true, "moonbeam",
            "info@moonbeam.test", "01-4000000", "PAN12345", "https://moonbeam.test", Guid.NewGuid());
        db.Organizations.Add(organization);
        return organization;
    }
}
