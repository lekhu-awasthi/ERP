using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.SetCustomStatus;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Manufacturing.Commands.CreateProductionOrder;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseOrder;
using ErpApp.Application.Sales.Commands.CreateQuotation;
using ErpApp.Application.Sales.Commands.CreateSalesOrder;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Contacts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class SetCustomStatusCommandHandlerTests
{
    [Fact]
    public async Task Handle_assigns_a_custom_status_to_a_quotation()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetCustomStatusCommandHandler(db);
        await handler.Handle(
            new SetCustomStatusCommand(seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, seed.QuotationStatusId),
            CancellationToken.None);

        var quotation = await db.Quotations.SingleAsync(x => x.Id == seed.QuotationId);
        Assert.Equal(seed.QuotationStatusId, quotation.CustomStatusId);
    }

    [Fact]
    public async Task Handle_assigns_a_custom_status_to_a_purchase_order()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetCustomStatusCommandHandler(db);
        await handler.Handle(
            new SetCustomStatusCommand(
                seed.OrganizationId, DocumentType.PurchaseOrder, seed.PurchaseOrderId, seed.PurchaseOrderStatusId),
            CancellationToken.None);

        var purchaseOrder = await db.PurchaseOrders.SingleAsync(x => x.Id == seed.PurchaseOrderId);
        Assert.Equal(seed.PurchaseOrderStatusId, purchaseOrder.CustomStatusId);
    }

    [Fact]
    public async Task Handle_null_clears_a_previously_assigned_status()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var handler = new SetCustomStatusCommandHandler(db);
        await handler.Handle(
            new SetCustomStatusCommand(seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, seed.QuotationStatusId),
            CancellationToken.None);

        await handler.Handle(
            new SetCustomStatusCommand(seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, null),
            CancellationToken.None);

        var quotation = await db.Quotations.SingleAsync(x => x.Id == seed.QuotationId);
        Assert.Null(quotation.CustomStatusId);
    }

    [Fact]
    public async Task Handle_rejects_a_status_defined_for_a_different_document_type()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var handler = new SetCustomStatusCommandHandler(db);

        // seed.PurchaseOrderStatusId was defined for DocumentType.PurchaseOrder, not Quotation.
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new SetCustomStatusCommand(seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, seed.PurchaseOrderStatusId),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_rejects_an_inactive_custom_status()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var inactiveStatus = await db.CustomStatuses.SingleAsync(x => x.Id == seed.QuotationStatusId);
        inactiveStatus.Update(inactiveStatus.Name, inactiveStatus.DocumentType, isActive: false);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new SetCustomStatusCommandHandler(db);
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new SetCustomStatusCommand(seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, seed.QuotationStatusId),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_rejects_a_custom_status_from_another_organization()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var otherOrgStatus = CustomStatus.Create(Guid.NewGuid(), "Pending", DocumentType.Quotation);
        db.CustomStatuses.Add(otherOrgStatus);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new SetCustomStatusCommandHandler(db);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SetCustomStatusCommand(seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, otherOrgStatus.Id),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_rejects_a_document_id_that_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var handler = new SetCustomStatusCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SetCustomStatusCommand(seed.OrganizationId, DocumentType.Quotation, Guid.NewGuid(), seed.QuotationStatusId),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_for_an_unwired_document_type()
    {
        var db = TestAppDbContext.Create();
        var handler = new SetCustomStatusCommandHandler(db);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.Handle(
            new SetCustomStatusCommand(Guid.NewGuid(), DocumentType.Invoice, Guid.NewGuid(), null), CancellationToken.None));
    }

    [Fact]
    public void Command_permission_key_rides_on_the_document_types_own_edit_permission()
    {
        var quotationCommand = new SetCustomStatusCommand(Guid.NewGuid(), DocumentType.Quotation, Guid.NewGuid(), null);
        Assert.Equal("Sales.Quotation.Edit", quotationCommand.PermissionKey);

        var purchaseOrderCommand = new SetCustomStatusCommand(Guid.NewGuid(), DocumentType.PurchaseOrder, Guid.NewGuid(), null);
        Assert.Equal("Purchasing.PurchaseOrder.Edit", purchaseOrderCommand.PermissionKey);

        var unsupported = new SetCustomStatusCommand(Guid.NewGuid(), DocumentType.Invoice, Guid.NewGuid(), null);
        Assert.Throws<ArgumentOutOfRangeException>(() => unsupported.PermissionKey);
    }

    /// <summary>
    /// Phase 27a: Sales Order was 20b's named "mechanical follow-up" and is now wired. Live-confirmed
    /// on the Sales Orders list grid's STAGE column, on Draft rows as well as Approved ones.
    /// </summary>
    [Fact]
    public async Task Handle_assigns_a_custom_status_to_a_sales_order()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetCustomStatusCommandHandler(db);
        await handler.Handle(
            new SetCustomStatusCommand(seed.OrganizationId, DocumentType.SalesOrder, seed.SalesOrderId, seed.SalesOrderStatusId),
            CancellationToken.None);

        var salesOrder = await db.SalesOrders.SingleAsync(x => x.Id == seed.SalesOrderId);
        Assert.Equal(seed.SalesOrderStatusId, salesOrder.CustomStatusId);
    }

    /// <summary>
    /// Phase 27a: Production Order, the fourth and last type with a live picker. Its list column is
    /// labelled STATUS rather than STAGE, but it is the same control over the same lookup.
    /// </summary>
    [Fact]
    public async Task Handle_assigns_a_custom_status_to_a_production_order()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetCustomStatusCommandHandler(db);
        await handler.Handle(
            new SetCustomStatusCommand(
                seed.OrganizationId, DocumentType.ProductionOrder, seed.ProductionOrderId, seed.ProductionOrderStatusId),
            CancellationToken.None);

        var productionOrder = await db.ProductionOrders.SingleAsync(x => x.Id == seed.ProductionOrderId);
        Assert.Equal(seed.ProductionOrderStatusId, productionOrder.CustomStatusId);
    }

    /// <summary>
    /// Phase 27a: a status defined for a different document type is still refused. Worth restating
    /// now that four types share one command -- the cross-type check is the only thing stopping a
    /// Sales Order from being stamped with a Production Order's pipeline value.
    /// </summary>
    [Fact]
    public async Task Handle_refuses_a_status_defined_for_a_different_document_type()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetCustomStatusCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new SetCustomStatusCommand(
                seed.OrganizationId, DocumentType.SalesOrder, seed.SalesOrderId, seed.ProductionOrderStatusId),
            CancellationToken.None));
    }

    private sealed record Seed(
        Guid OrganizationId,
        Guid QuotationId,
        Guid PurchaseOrderId,
        Guid SalesOrderId,
        Guid ProductionOrderId,
        Guid QuotationStatusId,
        Guid PurchaseOrderStatusId,
        Guid SalesOrderStatusId,
        Guid ProductionOrderStatusId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();

        var customer = await new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);

        var supplier = await new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateContactCommand(organizationId, ContactType.Supplier, "Acme Supplies", null, null, null, null, null, 0m),
            CancellationToken.None);

        var quotation = await new CreateQuotationCommandHandler(db).Handle(
            new CreateQuotationCommand(organizationId, customer.Id, new DateOnly(2026, 1, 1), null, null, []),
            CancellationToken.None);

        var purchaseOrder = await new CreatePurchaseOrderCommandHandler(db).Handle(
            new CreatePurchaseOrderCommand(organizationId, supplier.Id, new DateOnly(2026, 1, 1), null, []),
            CancellationToken.None);

        var salesOrder = await new CreateSalesOrderCommandHandler(db).Handle(
            new CreateSalesOrderCommand(organizationId, customer.Id, new DateOnly(2026, 1, 1), null, null, []),
            CancellationToken.None);

        // A Production Order needs a real output Product; nothing here exercises the catalog, so it
        // is built directly rather than through CreateProductCommand's own validation chain.
        var product = Product.Create(
            organizationId, ProductType.Goods, "Widget", "P0001", Guid.NewGuid(), Guid.NewGuid(), null,
            availableForSale: true, 0m, 0m, VatRate.NoVat, 0, trackInventory: true);
        db.Products.Add(product);
        await db.SaveChangesAsync(CancellationToken.None);

        var productionOrder = await new CreateProductionOrderCommandHandler(db).Handle(
            new CreateProductionOrderCommand(
                organizationId, new DateOnly(2026, 1, 1), null, product.Id, 10m, null, null, [], [], []),
            CancellationToken.None);

        var quotationStatus = CustomStatus.Create(organizationId, "Accepted", DocumentType.Quotation);
        var purchaseOrderStatus = CustomStatus.Create(organizationId, "Confirmed", DocumentType.PurchaseOrder);
        var salesOrderStatus = CustomStatus.Create(organizationId, "Packaged", DocumentType.SalesOrder);
        var productionOrderStatus = CustomStatus.Create(organizationId, "Completed", DocumentType.ProductionOrder);
        db.CustomStatuses.AddRange(quotationStatus, purchaseOrderStatus, salesOrderStatus, productionOrderStatus);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(
            organizationId,
            quotation.Id,
            purchaseOrder.Id,
            salesOrder.Id,
            productionOrder.Id,
            quotationStatus.Id,
            purchaseOrderStatus.Id,
            salesOrderStatus.Id,
            productionOrderStatus.Id);
    }
}
