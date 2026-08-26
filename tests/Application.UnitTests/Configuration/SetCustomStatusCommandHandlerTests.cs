using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.SetCustomStatus;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseOrder;
using ErpApp.Application.Sales.Commands.CreateQuotation;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
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

    private sealed record Seed(
        Guid OrganizationId, Guid QuotationId, Guid PurchaseOrderId, Guid QuotationStatusId, Guid PurchaseOrderStatusId);

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

        var quotationStatus = CustomStatus.Create(organizationId, "Accepted", DocumentType.Quotation);
        var purchaseOrderStatus = CustomStatus.Create(organizationId, "Confirmed", DocumentType.PurchaseOrder);
        db.CustomStatuses.AddRange(quotationStatus, purchaseOrderStatus);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, quotation.Id, purchaseOrder.Id, quotationStatus.Id, purchaseOrderStatus.Id);
    }
}
