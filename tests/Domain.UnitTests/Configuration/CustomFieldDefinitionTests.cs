using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class CustomFieldDefinitionTests
{
    [Fact]
    public void Create_starts_active_with_given_type_and_applicable_document_types()
    {
        var definition = CustomFieldDefinition.Create(
            Guid.NewGuid(), "PO Reference", CustomFieldType.Text, [DocumentType.Invoice, DocumentType.PurchaseBill], []);

        Assert.Equal("PO Reference", definition.Name);
        Assert.Equal(CustomFieldType.Text, definition.Type);
        Assert.Equal([DocumentType.Invoice, DocumentType.PurchaseBill], definition.ApplicableDocumentTypes);
        Assert.Empty(definition.ChoiceOptions);
        Assert.True(definition.IsActive);
    }

    [Fact]
    public void Create_stores_choice_options_for_a_choices_type_field()
    {
        var definition = CustomFieldDefinition.Create(
            Guid.NewGuid(), "Color", CustomFieldType.Choices, [DocumentType.Invoice], ["Red", "Blue"]);

        Assert.Equal(CustomFieldType.Choices, definition.Type);
        Assert.Equal(["Red", "Blue"], definition.ChoiceOptions);
    }

    [Fact]
    public void Update_replaces_name_type_applicable_document_types_active_flag_and_choice_options()
    {
        var definition = CustomFieldDefinition.Create(
            Guid.NewGuid(), "PO Reference", CustomFieldType.Text, [DocumentType.Invoice], []);

        definition.Update("Delivery Notes", CustomFieldType.Choices, [DocumentType.PurchaseBill], false, ["A", "B"]);

        Assert.Equal("Delivery Notes", definition.Name);
        Assert.Equal(CustomFieldType.Choices, definition.Type);
        Assert.Equal([DocumentType.PurchaseBill], definition.ApplicableDocumentTypes);
        Assert.Equal(["A", "B"], definition.ChoiceOptions);
        Assert.False(definition.IsActive);
    }
}
