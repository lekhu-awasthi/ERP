using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.CreateCustomStatus;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class CreateCustomStatusCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_custom_status_scoped_to_document_type()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateCustomStatusCommandHandler(db);

        var result = await handler.Handle(
            new CreateCustomStatusCommand(organizationId, "Awaiting Approval", DocumentType.Invoice), CancellationToken.None);

        var status = await db.CustomStatuses.SingleAsync(x => x.Id == result.Id);
        Assert.Equal(DocumentType.Invoice, status.DocumentType);
    }

    [Fact]
    public async Task Handle_allows_the_same_name_on_a_different_document_type()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.CustomStatuses.Add(CustomStatus.Create(organizationId, "Cleared", DocumentType.Invoice));
        await db.SaveChangesAsync();

        var handler = new CreateCustomStatusCommandHandler(db);

        var result = await handler.Handle(
            new CreateCustomStatusCommand(organizationId, "Cleared", DocumentType.PurchaseBill), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Handle_throws_conflict_for_duplicate_name_on_the_same_document_type()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.CustomStatuses.Add(CustomStatus.Create(organizationId, "Cleared", DocumentType.Invoice));
        await db.SaveChangesAsync();

        var handler = new CreateCustomStatusCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateCustomStatusCommand(organizationId, "Cleared", DocumentType.Invoice), CancellationToken.None));
    }
}
