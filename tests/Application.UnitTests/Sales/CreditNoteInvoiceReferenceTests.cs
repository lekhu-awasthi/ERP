using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Commands.UpdateCreditNote;
using ErpApp.Application.Sales.Commands.VoidInvoice;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.GetCreditNote;
using ErpApp.Application.Sales.Queries.GetCreditNoteConversionTemplate;
using ErpApp.Application.Sales.Queries.ListCreditableInvoices;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Sales;

/// <summary>
/// Phase 69 -- a credit note names the tax invoice it relates to (VAT Rules Rule 20(1)(e)):
/// <list type="bullet">
/// <item>a VAT-registered seller cannot approve a note naming none (Decision A);</item>
/// <item>a standalone note naming an invoice is a price adjustment, held to the invoice's customer,
/// currency, location and date, and with every other note never credits more than the invoice or its VAT
/// (Decision B);</item>
/// <item>an invoice issued before the system may be typed, dated before the first invoice here
/// (Decision C);</item>
/// <item>and the two bugs found while planning: a conversion's template dropped the invoice's currency,
/// and an edit of a converted draft skipped the line caps.</item>
/// </list>
/// </summary>
public class CreditNoteInvoiceReferenceTests
{
    private const decimal UsdRate = 133m;
    private static readonly DateOnly InvoiceDay = new(2026, 9, 1);
    private static readonly DateOnly NoteDay = new(2026, 9, 10);

    [Fact]
    public async Task A_price_adjustment_naming_its_invoice_is_approved_on_a_VAT_registered_tenant()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);

        var note = await CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m, reason: " Agreed a lower price ");
        await ApproveCreditNoteAsync(db, seed, note);

        var stored = await db.CreditNotes.SingleAsync(x => x.Id == note);
        Assert.Equal(CreditNoteStatus.Approved, stored.Status);
        Assert.Equal(invoiceId, stored.AgainstInvoiceId);
        Assert.Equal(invoiceId, stored.RelatedInvoiceId);
        Assert.Null(stored.ReferrerId);
        Assert.Equal("Agreed a lower price", stored.Reason);
    }

    [Fact]
    public async Task A_VAT_registered_tenant_cannot_approve_a_note_naming_no_invoice()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var note = await CreateStandaloneAsync(db, seed, rate: 100m);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => ApproveCreditNoteAsync(db, seed, note));

        Assert.Equal(CreditNote.MissingInvoiceReferenceMessage, refused.Message);
        Assert.Equal(CreditNoteStatus.Draft, (await db.CreditNotes.SingleAsync(x => x.Id == note)).Status);
    }

    [Fact]
    public async Task A_tenant_that_is_not_VAT_registered_approves_a_note_naming_no_invoice()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: false);
        var note = await CreateStandaloneAsync(db, seed, rate: 100m);

        await ApproveCreditNoteAsync(db, seed, note);

        Assert.Equal(CreditNoteStatus.Approved, (await db.CreditNotes.SingleAsync(x => x.Id == note)).Status);
    }

    [Fact]
    public async Task A_conversion_names_its_invoice_already_and_is_approved_on_a_VAT_registered_tenant()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);

        var note = await CreateConversionAsync(db, seed, invoiceId, quantity: 1);
        await ApproveCreditNoteAsync(db, seed, note);

        Assert.Equal(CreditNoteStatus.Approved, (await db.CreditNotes.SingleAsync(x => x.Id == note)).Status);
    }

    [Fact]
    public async Task An_adjustment_naming_another_customers_invoice_is_refused()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m, customerId: seed.OtherCustomerId);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m));
        Assert.Contains("another customer", refused.Message);
    }

    [Fact]
    public async Task An_adjustment_naming_a_draft_invoice_is_refused()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m, approve: false);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m));
        Assert.Contains("draft", refused.Message);
    }

    [Fact]
    public async Task An_adjustment_naming_a_till_sale_is_refused()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);
        await SetInvoiceAsync(db, invoiceId, (entry) => entry.Property(x => x.Channel).CurrentValue = SalesChannel.Pos);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m));
        Assert.Contains("till", refused.Message);
    }

    [Fact]
    public async Task An_adjustment_in_another_currency_from_its_invoice_is_refused()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 10m, currencyCode: "USD");

        var refused = await Assert.ThrowsAsync<ConflictException>(() => CreateAdjustmentAsync(db, seed, invoiceId, rate: 1m));
        Assert.Contains("USD", refused.Message);
    }

    [Fact]
    public async Task An_adjustment_at_another_billing_location_from_its_invoice_is_refused()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);
        await SetInvoiceAsync(db, invoiceId, (entry) => entry.Property(x => x.LocationId).CurrentValue = Guid.NewGuid());

        var refused = await Assert.ThrowsAsync<ConflictException>(() => CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m));
        Assert.Contains("billing location", refused.Message);
    }

    [Fact]
    public async Task An_adjustment_dated_before_its_invoice_is_refused()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);

        var refused = await Assert.ThrowsAsync<ConflictException>(
            () => CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m, date: InvoiceDay.AddDays(-1)));
        Assert.Contains("dated before", refused.Message);
    }

    [Fact]
    public async Task Adjustments_together_never_credit_more_than_the_invoice()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        // 2 x 1000 + 13% = 2,260.
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);

        // 1,500 + 195 = 1,695 credited; 565 left.
        await CreateAdjustmentAsync(db, seed, invoiceId, rate: 1500m);

        // 600 + 78 = 678 > 565.
        var refused = await Assert.ThrowsAsync<ConflictException>(() => CreateAdjustmentAsync(db, seed, invoiceId, rate: 600m));
        Assert.Contains("565.00", refused.Message);

        // 500 + 65 = 565 fits exactly.
        await CreateAdjustmentAsync(db, seed, invoiceId, rate: 500m);
    }

    [Fact]
    public async Task An_adjustment_cannot_give_back_VAT_the_invoice_never_charged()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m, vatRate: VatRate.NoVat);

        // 100 + 13 = 113 is well inside the 2,000 total, but its 13 of VAT was never charged.
        var refused = await Assert.ThrowsAsync<ConflictException>(() => CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m));
        Assert.Contains("VAT", refused.Message);

        await CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m, vatRate: VatRate.NoVat);
    }

    [Fact]
    public async Task A_return_after_an_adjustment_is_held_to_what_is_left_of_the_invoices_value()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);
        await CreateAdjustmentAsync(db, seed, invoiceId, rate: 1000m);

        // Both units back at 1,000 would credit 2,260 on top of the 1,130 already credited.
        var refused = await Assert.ThrowsAsync<ConflictException>(() => CreateConversionAsync(db, seed, invoiceId, quantity: 2));
        Assert.Contains("left to credit", refused.Message);

        await CreateConversionAsync(db, seed, invoiceId, quantity: 1);
    }

    [Fact]
    public async Task Editing_an_adjustment_counts_every_other_note_but_not_its_own_saved_lines()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);
        var note = await CreateAdjustmentAsync(db, seed, invoiceId, rate: 1500m);

        // Its own 1,695 is replaced, not added to: 2,000 + 260 = 2,260 is the whole invoice.
        await UpdateAdjustmentAsync(db, seed, note, invoiceId, rate: 2000m);

        await Assert.ThrowsAsync<ConflictException>(() => UpdateAdjustmentAsync(db, seed, note, invoiceId, rate: 2001m));
    }

    [Fact]
    public async Task An_invoice_named_by_an_adjustment_cannot_be_voided()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);
        await CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m);

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            new VoidInvoiceCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()), new StockLedgerService(db))
                .Handle(new VoidInvoiceCommand(seed.OrganizationId, invoiceId), CancellationToken.None));
        Assert.Contains("credit note", refused.Message);
    }

    [Fact]
    public async Task A_typed_invoice_older_than_the_first_invoice_here_is_accepted_and_approved()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);

        var note = await CreateTypedAsync(db, seed, "OLD-0042", InvoiceDay.AddDays(-30));
        await ApproveCreditNoteAsync(db, seed, note);

        var stored = await db.CreditNotes.SingleAsync(x => x.Id == note);
        Assert.Equal("OLD-0042", stored.AgainstInvoiceNumber);
        Assert.Null(stored.RelatedInvoiceId);
        Assert.Equal(CreditNoteStatus.Approved, stored.Status);
    }

    [Fact]
    public async Task A_typed_invoice_dated_on_or_after_the_first_invoice_here_is_refused_naming_the_field()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => CreateTypedAsync(db, seed, "INV-X", InvoiceDay));

        Assert.Equal(nameof(CreditNote.AgainstInvoiceDate), Assert.Single(refused.Errors).PropertyName);
    }

    [Fact]
    public async Task A_tenant_with_no_invoice_yet_may_type_any_date()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);

        var note = await CreateTypedAsync(db, seed, "OLD-0001", NoteDay);

        Assert.Equal(NoteDay, (await db.CreditNotes.SingleAsync(x => x.Id == note)).AgainstInvoiceDate);
    }

    [Fact]
    public async Task The_detail_names_the_related_invoice_for_a_return_and_for_an_adjustment()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);
        var adjustment = await CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m, reason: "Price cut");
        var conversion = await CreateConversionAsync(db, seed, invoiceId, quantity: 1);

        var adjustmentDto = await new GetCreditNoteQueryHandler(db).Handle(
            new GetCreditNoteQuery(seed.OrganizationId, adjustment), CancellationToken.None);
        var conversionDto = await new GetCreditNoteQueryHandler(db).Handle(
            new GetCreditNoteQuery(seed.OrganizationId, conversion), CancellationToken.None);

        Assert.Equal(invoiceId, adjustmentDto.AgainstInvoiceId);
        Assert.Equal("Price cut", adjustmentDto.Reason);
        Assert.Equal(invoiceId, adjustmentDto.RelatedInvoice!.Id);
        Assert.Equal(InvoiceDay, adjustmentDto.RelatedInvoice.Date);
        Assert.Equal(2260m, adjustmentDto.RelatedInvoice.GrandTotal);
        Assert.Null(conversionDto.AgainstInvoiceId);
        Assert.Equal(invoiceId, conversionDto.RelatedInvoice!.Id);
    }

    [Fact]
    public async Task The_PDF_prints_the_invoice_and_the_reason_for_an_adjustment_and_a_typed_invoice()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);
        var invoiceCode = (await db.Invoices.SingleAsync(x => x.Id == invoiceId)).Code;
        var adjustment = await CreateAdjustmentAsync(db, seed, invoiceId, rate: 100m, reason: "Price cut");
        var typed = await CreateTypedAsync(db, seed, "OLD-0042", new DateOnly(2026, 7, 1));
        await ApproveCreditNoteAsync(db, seed, adjustment);
        await ApproveCreditNoteAsync(db, seed, typed);

        var adjustmentPdf = await PrintAsync(db, seed, adjustment);
        var typedPdf = await PrintAsync(db, seed, typed);

        Assert.Contains(adjustmentPdf.HeaderFields, f => f.Label == "Against Invoice" && f.Value == $"{invoiceCode} dated 2026-09-01");
        Assert.Contains(adjustmentPdf.HeaderFields, f => f.Label == "Reason" && f.Value == "Price cut");
        Assert.Contains(typedPdf.HeaderFields, f => f.Label == "Against Invoice" && f.Value == "OLD-0042 dated 2026-07-01");
        Assert.DoesNotContain(typedPdf.HeaderFields, f => f.Label == "Reason");
    }

    [Fact]
    public async Task The_picker_lists_the_customers_approved_ERP_invoices_with_what_is_left_to_credit()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var partly = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);
        var whole = await ApprovedInvoiceAsync(db, seed, quantity: 1, rate: 1000m, date: InvoiceDay.AddDays(1));
        await ApprovedInvoiceAsync(db, seed, quantity: 1, rate: 1000m, approve: false);
        await ApprovedInvoiceAsync(db, seed, quantity: 1, rate: 1000m, customerId: seed.OtherCustomerId);
        var tillSale = await ApprovedInvoiceAsync(db, seed, quantity: 1, rate: 1000m);
        await SetInvoiceAsync(db, tillSale, (entry) => entry.Property(x => x.Channel).CurrentValue = SalesChannel.Pos);

        var adjustment = await CreateAdjustmentAsync(db, seed, partly, rate: 500m);
        await CreateConversionAsync(db, seed, whole, quantity: 1);

        var page = await new ListCreditableInvoicesQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new ListCreditableInvoicesQuery(seed.OrganizationId, seed.CustomerId), CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal([whole, partly], page.Items.Select(x => x.Id));
        var partlyRow = page.Items.Single(x => x.Id == partly);
        Assert.Equal(2260m, partlyRow.GrandTotal);
        Assert.Equal(565m, partlyRow.CreditedTotal);
        Assert.Equal(1695m, partlyRow.RemainingTotal);
        Assert.Equal(0m, page.Items.Single(x => x.Id == whole).RemainingTotal);

        // The draft being edited does not count against the invoice it names, as on the server's cap.
        var editing = await new ListCreditableInvoicesQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new ListCreditableInvoicesQuery(seed.OrganizationId, seed.CustomerId, ExcludingCreditNoteId: adjustment),
            CancellationToken.None);
        Assert.Equal(2260m, editing.Items.Single(x => x.Id == partly).RemainingTotal);
    }

    [Fact]
    public async Task A_converted_foreign_invoice_keeps_its_currency_and_credits_what_it_debited()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 10m, currencyCode: "USD");

        // The client's conversion flow: the template, sent back as it came.
        var template = await new GetCreditNoteConversionTemplateQueryHandler(db).Handle(
            new GetCreditNoteConversionTemplateQuery(seed.OrganizationId, invoiceId), CancellationToken.None);
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, template.ContactId, NoteDay, template.Reference, template.Lines,
                template.ReferrerType, template.ReferrerId, template.DiscountPct, template.Terms)
            {
                CurrencyCode = template.CurrencyCode,
                ExchangeRate = template.ExchangeRate,
                LocationId = template.LocationId,
            },
            CancellationToken.None);
        await ApproveCreditNoteAsync(db, seed, created.Id);

        Assert.Equal("USD", template.CurrencyCode);
        Assert.Equal(UsdRate, template.ExchangeRate);
        var invoiceDebit = await ArLineAsync(db, seed, DocumentType.Invoice, invoiceId, debit: true);
        var noteCredit = await ArLineAsync(db, seed, DocumentType.CreditNote, created.Id, debit: false);
        Assert.Equal(invoiceDebit, noteCredit);
        Assert.Equal(22.60m * UsdRate, noteCredit);
    }

    [Fact]
    public async Task A_conversion_in_another_currency_from_its_invoice_is_refused()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 10m, currencyCode: "USD");

        var refused = await Assert.ThrowsAsync<ConflictException>(
            () => CreateConversionAsync(db, seed, invoiceId, quantity: 1, rate: 10m));
        Assert.Contains("USD", refused.Message);
    }

    [Fact]
    public async Task Editing_a_converted_draft_is_held_to_what_is_left_of_each_line()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        // Two lines, 2 x 1000 and 2 x 500 (3,390 in all), so three units at 1,000 (3,390) fit the
        // invoice's value and only the per-line cap can refuse them.
        var invoiceId = await ApprovedInvoiceAsync(
            db, seed, quantity: 2, rate: 1000m,
            extraLine: new InvoiceLineInput(seed.ProductId, 2m, 500m, VatRate.ThirteenPercentVat));
        var note = await CreateConversionAsync(db, seed, invoiceId, quantity: 1);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => new UpdateCreditNoteCommandHandler(db).Handle(
            new UpdateCreditNoteCommand(
                seed.OrganizationId, note, seed.CustomerId, NoteDay, null,
                [new CreditNoteLineInput(seed.ProductId, 3m, 1000m, VatRate.ThirteenPercentVat, 0)]),
            CancellationToken.None));
        Assert.Contains("only 2 remains", refused.Message);

        // Its own saved quantity is not counted against it: 2 is the whole line.
        await new UpdateCreditNoteCommandHandler(db).Handle(
            new UpdateCreditNoteCommand(
                seed.OrganizationId, note, seed.CustomerId, NoteDay, null,
                [new CreditNoteLineInput(seed.ProductId, 2m, 1000m, VatRate.ThirteenPercentVat, 0)]),
            CancellationToken.None);
    }

    [Fact]
    public async Task A_converted_draft_refuses_a_second_reference_naming_the_field()
    {
        var (db, seed) = await SeedAsync(isVatRegistered: true);
        var invoiceId = await ApprovedInvoiceAsync(db, seed, quantity: 2, rate: 1000m);
        var note = await CreateConversionAsync(db, seed, invoiceId, quantity: 1);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => new UpdateCreditNoteCommandHandler(db).Handle(
            new UpdateCreditNoteCommand(
                seed.OrganizationId, note, seed.CustomerId, NoteDay, null,
                [new CreditNoteLineInput(seed.ProductId, 1m, 1000m, VatRate.ThirteenPercentVat, 0)])
            {
                AgainstInvoiceId = invoiceId,
            },
            CancellationToken.None));

        Assert.Equal(nameof(CreditNote.AgainstInvoiceId), Assert.Single(refused.Errors).PropertyName);
    }

    [Theory]
    [InlineData(true, "OLD-1", true, "AgainstInvoiceId")]
    [InlineData(false, "OLD-1", false, "AgainstInvoiceDate")]
    [InlineData(false, null, true, "AgainstInvoiceNumber")]
    public void The_validators_name_the_field_a_malformed_reference_breaks(
        bool picked, string? number, bool dated, string field)
    {
        var create = new CreateCreditNoteCommand(Guid.NewGuid(), Guid.NewGuid(), NoteDay, null, [])
        {
            AgainstInvoiceId = picked ? Guid.NewGuid() : null,
            AgainstInvoiceNumber = number,
            AgainstInvoiceDate = dated ? InvoiceDay : null,
        };
        var update = new UpdateCreditNoteCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NoteDay, null, [])
        {
            AgainstInvoiceId = create.AgainstInvoiceId,
            AgainstInvoiceNumber = create.AgainstInvoiceNumber,
            AgainstInvoiceDate = create.AgainstInvoiceDate,
        };

        Assert.Contains(new CreateCreditNoteCommandValidator().Validate(create).Errors, e => e.PropertyName == field);
        Assert.Contains(new UpdateCreditNoteCommandValidator().Validate(update).Errors, e => e.PropertyName == field);
    }

    [Fact]
    public void The_create_validator_refuses_a_conversion_that_also_names_an_invoice()
    {
        var command = new CreateCreditNoteCommand(
            Guid.NewGuid(), Guid.NewGuid(), NoteDay, null, [], DocumentType.Invoice, Guid.NewGuid())
        {
            AgainstInvoiceId = Guid.NewGuid(),
        };

        Assert.Contains(new CreateCreditNoteCommandValidator().Validate(command).Errors, e => e.PropertyName == "AgainstInvoiceId");
        Assert.True(new ListCreditableInvoicesQueryValidator()
            .Validate(new ListCreditableInvoicesQuery(Guid.NewGuid(), Guid.Empty)).Errors.Count > 0);
    }

    // ---------------------------------------------------------------------------------------------

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid OtherCustomerId,
        Guid WarehouseId, Guid ProductId, Guid ReceivableAccountId);

    private static async Task<(IAppDbContext Db, Seed Seed)> SeedAsync(bool isVatRegistered)
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", "Kathmandu, Nepal", new DateOnly(2026, 1, 1), isVatRegistered, "moonbeam",
            "info@moonbeam.test", "01-4000000", "PAN12345", "https://moonbeam.test", Guid.NewGuid());
        db.Organizations.Add(organization);
        var organizationId = organization.Id;
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);
        var other = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Zenith Stores", null, null, null, null, null, 0m),
            CancellationToken.None);
        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);
        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "Services", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Unit", "u"), CancellationToken.None);
        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 1000m, 800m,
                VatRate.ThirteenPercentVat, 0, false),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var ar = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var vatPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Payable", liabilityGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, null, null, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return (db, new Seed(organizationId, numberGenerator, customer.Id, other.Id, warehouse.Id, product.Id, ar.Id));
    }

    private static async Task<Guid> ApprovedInvoiceAsync(
        IAppDbContext db, Seed seed, decimal quantity, decimal rate, Guid? customerId = null, bool approve = true,
        string? currencyCode = null, VatRate vatRate = VatRate.ThirteenPercentVat, DateOnly? date = null,
        InvoiceLineInput? extraLine = null)
    {
        List<InvoiceLineInput> lines = [new InvoiceLineInput(seed.ProductId, quantity, rate, vatRate)];
        if (extraLine is not null)
        {
            lines.Add(extraLine);
        }

        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, customerId ?? seed.CustomerId, seed.WarehouseId, date ?? InvoiceDay, null, lines)
            {
                CurrencyCode = currencyCode,
                ExchangeRate = currencyCode is null ? null : UsdRate,
            },
            CancellationToken.None);

        if (approve)
        {
            var stockLedgerService = new StockLedgerService(db);
            await new ApproveInvoiceCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService, new ContactCreditLimitPolicy(db))
                .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
        }

        return created.Id;
    }

    private static async Task SetInvoiceAsync(
        IAppDbContext db, Guid invoiceId, Action<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Invoice>> change)
    {
        // A state only another door produces (a till sale, a branch's invoice), reached through the change
        // tracker rather than by weakening an invariant (phase 31's rule).
        var invoice = await db.Invoices.SingleAsync(x => x.Id == invoiceId);
        change(((DbContext)db).Entry(invoice));
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<Guid> CreateStandaloneAsync(IAppDbContext db, Seed seed, decimal rate) =>
        (await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, NoteDay, null,
                [new CreditNoteLineInput(seed.ProductId, 1m, rate, VatRate.ThirteenPercentVat, 0)]),
            CancellationToken.None)).Id;

    private static async Task<Guid> CreateAdjustmentAsync(
        IAppDbContext db, Seed seed, Guid invoiceId, decimal rate, string? reason = null, DateOnly? date = null,
        VatRate vatRate = VatRate.ThirteenPercentVat) =>
        (await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date ?? NoteDay, null,
                [new CreditNoteLineInput(seed.ProductId, 1m, rate, vatRate, 0)])
            {
                AgainstInvoiceId = invoiceId,
                Reason = reason,
            },
            CancellationToken.None)).Id;

    private static async Task UpdateAdjustmentAsync(IAppDbContext db, Seed seed, Guid noteId, Guid invoiceId, decimal rate) =>
        await new UpdateCreditNoteCommandHandler(db).Handle(
            new UpdateCreditNoteCommand(
                seed.OrganizationId, noteId, seed.CustomerId, NoteDay, null,
                [new CreditNoteLineInput(seed.ProductId, 1m, rate, VatRate.ThirteenPercentVat, 0)])
            {
                AgainstInvoiceId = invoiceId,
            },
            CancellationToken.None);

    private static async Task<Guid> CreateConversionAsync(
        IAppDbContext db, Seed seed, Guid invoiceId, decimal quantity, decimal rate = 1000m) =>
        (await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, NoteDay, null,
                [new CreditNoteLineInput(seed.ProductId, quantity, rate, VatRate.ThirteenPercentVat, 0)],
                DocumentType.Invoice, invoiceId),
            CancellationToken.None)).Id;

    private static async Task<Guid> CreateTypedAsync(IAppDbContext db, Seed seed, string number, DateOnly date) =>
        (await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, NoteDay, null,
                [new CreditNoteLineInput(seed.ProductId, 1m, 100m, VatRate.ThirteenPercentVat, 0)])
            {
                AgainstInvoiceNumber = number,
                AgainstInvoiceDate = date,
            },
            CancellationToken.None)).Id;

    private static async Task ApproveCreditNoteAsync(IAppDbContext db, Seed seed, Guid noteId) =>
        await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, noteId), CancellationToken.None);

    private static async Task<decimal> ArLineAsync(IAppDbContext db, Seed seed, DocumentType type, Guid documentId, bool debit)
    {
        var lines = await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == type && x.SourceDocumentId == documentId)
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountId == seed.ReceivableAccountId)
            .ToListAsync();
        return lines.Sum(x => debit ? x.Debit : x.Credit);
    }

    private static async Task<PrintableDocumentDto> PrintAsync(IAppDbContext db, Seed seed, Guid noteId) =>
        await new PrintDocumentQueryHandler(db, new FakeFileStorage()).Handle(
            new PrintDocumentQuery(
                seed.OrganizationId, DocumentType.CreditNote, noteId,
                new DocumentPrintIssue(1, Guid.NewGuid(), DateTimeOffset.UtcNow)),
            CancellationToken.None);
}
