using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class CustomFieldDefinitionTests
{
    [Fact]
    public void Create_starts_active_with_given_type_and_applicable_document_types()
    {
        var definition = CustomFieldDefinition.Create(
            Guid.NewGuid(), "PO Reference", CustomFieldType.Text, [DocumentType.Invoice, DocumentType.PurchaseBill]);

        Assert.Equal("PO Reference", definition.Name);
        Assert.Equal(CustomFieldType.Text, definition.Type);
        Assert.Equal([DocumentType.Invoice, DocumentType.PurchaseBill], definition.ApplicableDocumentTypes);
        Assert.True(definition.IsActive);
    }

    [Fact]
    public void Update_replaces_name_type_applicable_document_types_and_active_flag()
    {
        var definition = CustomFieldDefinition.Create(
            Guid.NewGuid(), "PO Reference", CustomFieldType.Text, [DocumentType.Invoice]);

        definition.Update("Delivery Notes", CustomFieldType.Description, [DocumentType.PurchaseBill], false);

        Assert.Equal("Delivery Notes", definition.Name);
        Assert.Equal(CustomFieldType.Description, definition.Type);
        Assert.Equal([DocumentType.PurchaseBill], definition.ApplicableDocumentTypes);
        Assert.False(definition.IsActive);
    }
}
