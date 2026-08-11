using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.CreateCustomFieldDefinition;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class CreateCustomFieldDefinitionCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_definition_with_applicable_document_types()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateCustomFieldDefinitionCommandHandler(db);

        var result = await handler.Handle(
            new CreateCustomFieldDefinitionCommand(
                organizationId, "PO Reference", CustomFieldType.Text, [DocumentType.Invoice, DocumentType.PurchaseBill]),
            CancellationToken.None);

        var definition = await db.CustomFieldDefinitions.SingleAsync(x => x.Id == result.Id);
        Assert.Equal([DocumentType.Invoice, DocumentType.PurchaseBill], definition.ApplicableDocumentTypes);
    }

    [Fact]
    public async Task Handle_throws_conflict_for_duplicate_name_in_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.CustomFieldDefinitions.Add(
            CustomFieldDefinition.Create(organizationId, "PO Reference", CustomFieldType.Text, [DocumentType.Invoice]));
        await db.SaveChangesAsync();

        var handler = new CreateCustomFieldDefinitionCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateCustomFieldDefinitionCommand(organizationId, "PO Reference", CustomFieldType.Number, [DocumentType.PurchaseBill]),
            CancellationToken.None));
    }
}
