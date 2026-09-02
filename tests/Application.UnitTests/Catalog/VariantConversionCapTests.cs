using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Catalog;

/// <summary>
/// Decision E. Phase 6's bug #4 capped a CreditNote against the source Invoice's remaining
/// quantity per exact (ProductId, Rate, VatRate, DiscountPct) line. The Phase 24 question was
/// whether two variants sharing a Rate and VatRate collapse into one bucket and let a return of
/// Large-Blue be satisfied out of Large-Red's quantity.
///
/// They do not, and the reason is Decision A rather than anything added here: two variants are two
/// ProductIds, so the existing key already discriminates them. This is asserted rather than
/// reasoned about, because "the key already covers it" is exactly the kind of claim that is true
/// right up until the key changes.
/// </summary>
public class VariantConversionCapTests
{
    private const decimal SharedRate = 500m;
    private const VatRate SharedVat = VatRate.ThirteenPercentVat;

    private sealed record Fixture(
        IAppDbContext Db, Guid OrganizationId, Guid CustomerId, Guid WarehouseId, Guid BlueId, Guid RedId);

    private static async Task<Fixture> SeedAsync()
    {
        var db = TestAppDbContext.Create();
        var orgId = Guid.NewGuid();
        var colorId = Guid.NewGuid();
        var blue = Guid.NewGuid();
        var red = Guid.NewGuid();

        var parent = Product.Create(
            orgId, ProductType.Goods, "T-Shirt", "P-0001", Guid.NewGuid(), Guid.NewGuid(), null,
            true, SharedRate, 300m, SharedVat, 0, true);
        parent.SetVariantAttributeUsages([(colorId, blue), (colorId, red)]);

        // Deliberately identical prices and VAT: the whole point of the test.
        var blueVariant = parent.CreateVariant("P-0002", "T-Shirt Blue", [(colorId, blue)], SharedRate, 300m, null, null);
        var redVariant = parent.CreateVariant("P-0003", "T-Shirt Red", [(colorId, red)], SharedRate, 300m, null, null);

        var warehouse = Warehouse.Create(orgId, "Main");
        var customer = Contact.Create(orgId, ContactType.Customer, "Acme", "C-0001", null, null, null, null, null, 0m);

        db.Products.AddRange(parent, blueVariant, redVariant);
        db.Warehouses.Add(warehouse);
        db.Contacts.Add(customer);
        await db.SaveChangesAsync();

        return new Fixture(db, orgId, customer.Id, warehouse.Id, blueVariant.Id, redVariant.Id);
    }

    /// <summary>Invoices 3 Blue and 5 Red at the same Rate/VatRate, and approves it so the source
    /// is real.</summary>
    private static async Task<Guid> InvoiceThreeBlueAndFiveRedAsync(Fixture f)
    {
        var result = await new CreateInvoiceCommandHandler(f.Db).Handle(
            new CreateInvoiceCommand(
                f.OrganizationId, f.CustomerId, f.WarehouseId, new DateOnly(2026, 1, 1), null,
                [
                    new InvoiceLineInput(f.BlueId, 3m, SharedRate, SharedVat),
                    new InvoiceLineInput(f.RedId, 5m, SharedRate, SharedVat),
                ]),
            CancellationToken.None);

        var invoice = await f.Db.Invoices.SingleAsync(x => x.Id == result.Id);
        invoice.Approve(Guid.NewGuid(), "INV-0001");
        await f.Db.SaveChangesAsync();

        return invoice.Id;
    }

    private static CreateCreditNoteCommand CreditNote(Fixture f, Guid invoiceId, Guid productId, decimal quantity) =>
        new(f.OrganizationId, f.CustomerId, new DateOnly(2026, 1, 5), null,
            [new CreditNoteLineInput(productId, quantity, SharedRate, SharedVat)],
            DocumentType.Invoice, invoiceId);

    [Fact]
    public async Task A_variant_cannot_be_over_returned_by_borrowing_its_siblings_quantity()
    {
        // 3 Blue were invoiced. Returning 4 must fail even though 8 units at this exact
        // (Rate, VatRate, DiscountPct) were invoiced in total across the two variants.
        var f = await SeedAsync();
        var invoiceId = await InvoiceThreeBlueAndFiveRedAsync(f);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => new CreateCreditNoteCommandHandler(f.Db).Handle(
                CreditNote(f, invoiceId, f.BlueId, 4m), CancellationToken.None));

        Assert.Contains("only 3", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Each_variant_can_be_returned_up_to_its_own_invoiced_quantity()
    {
        var f = await SeedAsync();
        var invoiceId = await InvoiceThreeBlueAndFiveRedAsync(f);
        var handler = new CreateCreditNoteCommandHandler(f.Db);

        await handler.Handle(CreditNote(f, invoiceId, f.BlueId, 3m), CancellationToken.None);
        await handler.Handle(CreditNote(f, invoiceId, f.RedId, 5m), CancellationToken.None);

        Assert.Equal(2, await f.Db.CreditNotes.CountAsync());
    }

    [Fact]
    public async Task Returning_a_variant_that_was_never_on_the_invoice_is_rejected()
    {
        // The parent's own id shares the Rate and VatRate too, so this is the same trap from the
        // other direction -- and it is refused twice over, since a parent is not transactable.
        var f = await SeedAsync();
        var invoiceId = await InvoiceThreeBlueAndFiveRedAsync(f);
        var parentId = await f.Db.Products.Where(x => x.HasVariants).Select(x => x.Id).SingleAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => new CreateCreditNoteCommandHandler(f.Db).Handle(
                CreditNote(f, invoiceId, parentId, 1m), CancellationToken.None));
    }

    [Fact]
    public async Task Prior_returns_of_one_variant_do_not_consume_its_siblings_remaining_quantity()
    {
        var f = await SeedAsync();
        var invoiceId = await InvoiceThreeBlueAndFiveRedAsync(f);
        var handler = new CreateCreditNoteCommandHandler(f.Db);

        // Use up Blue entirely ...
        await handler.Handle(CreditNote(f, invoiceId, f.BlueId, 3m), CancellationToken.None);

        // ... Red must be untouched.
        await handler.Handle(CreditNote(f, invoiceId, f.RedId, 5m), CancellationToken.None);

        // ... and Blue must now be exhausted.
        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(CreditNote(f, invoiceId, f.BlueId, 1m), CancellationToken.None));
    }
}
