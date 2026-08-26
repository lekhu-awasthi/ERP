using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.CreateCustomFieldDefinition;
using ErpApp.Application.Configuration.Commands.SetCustomFieldValues;
using ErpApp.Application.Configuration.Queries.GetCustomFieldValues;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Sales.Commands.CreateQuotation;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Configuration;
using FluentValidation;

namespace ErpApp.Application.UnitTests.Configuration;

public class SetCustomFieldValuesCommandHandlerTests
{
    [Fact]
    public async Task Handle_replaces_the_full_value_set_rather_than_appending()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetCustomFieldValuesCommandHandler(db);
        await handler.Handle(
            new SetCustomFieldValuesCommand(
                seed.OrganizationId,
                DocumentType.Quotation,
                seed.QuotationId,
                [new CustomFieldValueInput(seed.TextFieldId, "first"), new CustomFieldValueInput(seed.ChoiceFieldId, "Red")]),
            CancellationToken.None);

        await handler.Handle(
            new SetCustomFieldValuesCommand(
                seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, [new CustomFieldValueInput(seed.TextFieldId, "second")]),
            CancellationToken.None);

        var values = await new GetCustomFieldValuesQueryHandler(db).Handle(
            new GetCustomFieldValuesQuery(seed.OrganizationId, DocumentType.Quotation, seed.QuotationId), CancellationToken.None);

        var value = Assert.Single(values);
        Assert.Equal(seed.TextFieldId, value.FieldDefinitionId);
        Assert.Equal("second", value.Value);
    }

    [Fact]
    public async Task Handle_skips_storing_a_blank_value()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetCustomFieldValuesCommandHandler(db);
        await handler.Handle(
            new SetCustomFieldValuesCommand(
                seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, [new CustomFieldValueInput(seed.TextFieldId, "")]),
            CancellationToken.None);

        var values = await new GetCustomFieldValuesQueryHandler(db).Handle(
            new GetCustomFieldValuesQuery(seed.OrganizationId, DocumentType.Quotation, seed.QuotationId), CancellationToken.None);

        Assert.Empty(values);
    }

    [Fact]
    public async Task Handle_rejects_a_choice_value_that_is_not_one_of_the_fields_options()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetCustomFieldValuesCommandHandler(db);
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new SetCustomFieldValuesCommand(
                seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, [new CustomFieldValueInput(seed.ChoiceFieldId, "Purple")]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_rejects_a_field_that_does_not_apply_to_the_documents_type()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var invoiceOnlyField = await new CreateCustomFieldDefinitionCommandHandler(db).Handle(
            new CreateCustomFieldDefinitionCommand(seed.OrganizationId, "Invoice Only", CustomFieldType.Text, [DocumentType.Invoice], []),
            CancellationToken.None);

        var handler = new SetCustomFieldValuesCommandHandler(db);
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new SetCustomFieldValuesCommand(
                seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, [new CustomFieldValueInput(invoiceOnlyField.Id, "x")]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_rejects_a_field_definition_that_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetCustomFieldValuesCommandHandler(db);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SetCustomFieldValuesCommand(
                seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, [new CustomFieldValueInput(Guid.NewGuid(), "x")]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_rejects_a_document_id_that_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetCustomFieldValuesCommandHandler(db);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SetCustomFieldValuesCommand(
                seed.OrganizationId, DocumentType.Quotation, Guid.NewGuid(), [new CustomFieldValueInput(seed.TextFieldId, "x")]),
            CancellationToken.None));
    }

    [Fact]
    public void Command_permission_key_rides_on_the_document_types_own_edit_permission()
    {
        var quotationCommand = new SetCustomFieldValuesCommand(Guid.NewGuid(), DocumentType.Quotation, Guid.NewGuid(), []);
        Assert.Equal("Sales.Quotation.Edit", quotationCommand.PermissionKey);

        var invoiceCommand = new SetCustomFieldValuesCommand(Guid.NewGuid(), DocumentType.Invoice, Guid.NewGuid(), []);
        Assert.Equal("Sales.Invoice.Edit", invoiceCommand.PermissionKey);

        var unsupported = new SetCustomFieldValuesCommand(Guid.NewGuid(), DocumentType.JournalVoucher, Guid.NewGuid(), []);
        Assert.Throws<ArgumentOutOfRangeException>(() => unsupported.PermissionKey);
    }

    private sealed record Seed(Guid OrganizationId, Guid QuotationId, Guid TextFieldId, Guid ChoiceFieldId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();

        var customer = await new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);

        var quotation = await new CreateQuotationCommandHandler(db).Handle(
            new CreateQuotationCommand(organizationId, customer.Id, new DateOnly(2026, 1, 1), null, null, []),
            CancellationToken.None);

        var textField = await new CreateCustomFieldDefinitionCommandHandler(db).Handle(
            new CreateCustomFieldDefinitionCommand(organizationId, "Batch No", CustomFieldType.Text, [DocumentType.Quotation], []),
            CancellationToken.None);

        var choiceField = await new CreateCustomFieldDefinitionCommandHandler(db).Handle(
            new CreateCustomFieldDefinitionCommand(
                organizationId, "Color", CustomFieldType.Choices, [DocumentType.Quotation], ["Red", "Blue"]),
            CancellationToken.None);

        return new Seed(organizationId, quotation.Id, textField.Id, choiceField.Id);
    }
}
